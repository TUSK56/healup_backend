$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$base = 'http://localhost:8000'

for ($i = 0; $i -lt 30; $i++) {
    try {
        Invoke-RestMethod -Uri "$base/swagger/v1/swagger.json" -Method Get | Out-Null
        break
    }
    catch {
        Start-Sleep -Milliseconds 500
    }
}

function CallApi {
    param(
        [string]$Method,
        [string]$Path,
        [object]$Body = $null,
        [string]$Token = $null,
        [string]$BodyType = 'json'
    )

    $headers = @{}
    if ($Token) {
        $headers['Authorization'] = "Bearer $Token"
    }

    $uri = "$base$Path"
    try {
        if ($null -eq $Body) {
            $data = Invoke-RestMethod -Uri $uri -Method $Method -Headers $headers
        }
        elseif ($BodyType -eq 'form') {
            $data = Invoke-RestMethod -Uri $uri -Method $Method -Headers $headers -Form $Body
        }
        else {
            $json = $Body | ConvertTo-Json -Depth 8
            $data = Invoke-RestMethod -Uri $uri -Method $Method -Headers $headers -Body $json -ContentType 'application/json'
        }

        return [pscustomobject]@{
            ok = $true
            status = 200
            data = $data
            raw = ''
        }
    }
    catch {
        $status = 0
        $raw = ''

        if ($_.Exception.Response) {
            try {
                $status = [int]$_.Exception.Response.StatusCode
            }
            catch {
                $status = 0
            }

            try {
                $reader = New-Object IO.StreamReader($_.Exception.Response.GetResponseStream())
                $raw = $reader.ReadToEnd()
                $reader.Close()
            }
            catch {
                $raw = $_.Exception.Message
            }
        }
        else {
            $raw = $_.Exception.Message
        }

        return [pscustomobject]@{
            ok = $false
            status = $status
            data = $null
            raw = $raw
        }
    }
}

$results = New-Object System.Collections.Generic.List[object]
function AddResult {
    param([string]$Endpoint, $Result)

    $results.Add([pscustomobject]@{
            endpoint = $Endpoint
            ok = $Result.ok
            status = $Result.status
            details = if ($Result.ok) { 'OK' } else { $Result.raw }
        })
}

function CallMultipartWithCurl {
    param(
        [string]$Path,
        [string]$Token,
        [string]$MedicinesJson
    )

    $url = "$base$Path"
    $httpClient = New-Object System.Net.Http.HttpClient
    $request = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Post, $url)
    $request.Headers.Authorization = New-Object System.Net.Http.Headers.AuthenticationHeaderValue('Bearer', $Token)

    $content = New-Object System.Net.Http.MultipartFormDataContent
    $content.Add((New-Object System.Net.Http.StringContent($MedicinesJson)), 'medicines')
    $request.Content = $content

    $response = $httpClient.SendAsync($request).Result
    $status = [int]$response.StatusCode
    $rawBody = $response.Content.ReadAsStringAsync().Result

    $request.Dispose()
    $content.Dispose()
    $httpClient.Dispose()

    if ($status -ge 200 -and $status -lt 300) {
        return [pscustomobject]@{
            ok = $true
            status = $status
            data = ($rawBody | ConvertFrom-Json)
            raw = $rawBody
        }
    }

    return [pscustomobject]@{
        ok = $false
        status = $status
        data = $null
        raw = $rawBody
    }
}

$ts = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$pEmail = "patient$ts@healup.test"
$phEmail = "pharmacy$ts@healup.test"
$password = 'Pass123!'

$r = CallApi -Method 'Post' -Path '/api/register/patient' -Body @{
    name = 'Patient Test'
    email = $pEmail
    phone = '0100000000'
    password = $password
    passwordConfirmation = $password
    latitude = 30.1
    longitude = 31.2
}
AddResult -Endpoint 'POST /api/register/patient' -Result $r
if (-not $r.ok) { $results | Format-Table -Wrap -AutoSize; exit 1 }
$patientToken = $r.data.token
$patientId = [int]$r.data.user.id

$r = CallApi -Method 'Post' -Path '/api/register/pharmacy' -Body @{
    name = 'Pharmacy Test'
    email = $phEmail
    phone = '0200000000'
    licenseNumber = 'LIC-1'
    password = $password
    passwordConfirmation = $password
    latitude = 30.2
    longitude = 31.3
}
AddResult -Endpoint 'POST /api/register/pharmacy' -Result $r
if (-not $r.ok) { $results | Format-Table -Wrap -AutoSize; exit 1 }
$pharmacyId = [int]$r.data.pharmacy.id

$r = CallApi -Method 'Post' -Path '/api/login' -Body @{ email = $phEmail; password = $password; guard = 'pharmacy' }
AddResult -Endpoint 'POST /api/login (pharmacy pending expected 403)' -Result ([pscustomobject]@{ ok = ($r.status -eq 403); status = $r.status; raw = $r.raw })
if ($r.status -ne 403) { $results | Format-Table -Wrap -AutoSize; exit 1 }

$connString = 'Server=TUSK\SQLEXPRESS;Database=healup_dotnet;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False'
$sqlConn = New-Object System.Data.SqlClient.SqlConnection $connString
$sqlConn.Open()
$cmd = $sqlConn.CreateCommand()
$cmd.CommandText = 'UPDATE users SET role=''admin'' WHERE id=@id'
$null = $cmd.Parameters.Add('@id', [System.Data.SqlDbType]::Int)
$cmd.Parameters['@id'].Value = $patientId
$cmd.ExecuteNonQuery() | Out-Null
$sqlConn.Close()

$r = CallApi -Method 'Post' -Path '/api/login' -Body @{ email = $pEmail; password = $password; guard = 'admin' }
AddResult -Endpoint 'POST /api/login (admin)' -Result $r
if (-not $r.ok) { $results | Format-Table -Wrap -AutoSize; exit 1 }
$adminToken = $r.data.token

$r = CallApi -Method 'Get' -Path '/api/admin/pharmacies' -Token $adminToken
AddResult -Endpoint 'GET /api/admin/pharmacies' -Result $r

$r = CallApi -Method 'Patch' -Path "/api/admin/pharmacies/$pharmacyId/approve" -Body @{} -Token $adminToken
AddResult -Endpoint 'PATCH /api/admin/pharmacies/{id}/approve' -Result $r

$r = CallApi -Method 'Post' -Path '/api/login' -Body @{ email = $phEmail; password = $password; guard = 'pharmacy' }
AddResult -Endpoint 'POST /api/login (pharmacy approved)' -Result $r
if (-not $r.ok) { $results | Format-Table -Wrap -AutoSize; exit 1 }
$pharmacyToken = $r.data.token

$r = CallMultipartWithCurl -Path '/api/requests' -Token $patientToken -MedicinesJson '[{"medicine_name":"Augmentin","quantity":2}]'
AddResult -Endpoint 'POST /api/requests' -Result $r
if (-not $r.ok) { $results | Format-Table -Wrap -AutoSize; exit 1 }
$requestId = [int]$r.data.request.id

$r = CallApi -Method 'Get' -Path '/api/requests' -Token $patientToken
AddResult -Endpoint 'GET /api/requests' -Result $r

$r = CallApi -Method 'Get' -Path "/api/requests/$requestId" -Token $patientToken
AddResult -Endpoint 'GET /api/requests/{id}' -Result $r

$r = CallApi -Method 'Get' -Path '/api/pharmacy/requests' -Token $pharmacyToken
AddResult -Endpoint 'GET /api/pharmacy/requests' -Result $r

$medicines = @(@{
        medicine_name = 'Augmentin'
        available = $true
        quantity_available = 2
        price = 15.5
    })
$r = CallApi -Method 'Post' -Path '/api/pharmacy/respond' -Token $pharmacyToken -Body @{
    request_id = $requestId
    delivery_fee = 5
    medicines = $medicines
}
AddResult -Endpoint 'POST /api/pharmacy/respond' -Result $r
if (-not $r.ok) { $results | Format-Table -Wrap -AutoSize; exit 1 }
$responseId = [int]$r.data.response.id

$r = CallApi -Method 'Get' -Path "/api/requests/$requestId/offers" -Token $patientToken
AddResult -Endpoint 'GET /api/requests/{id}/offers' -Result $r

$r = CallApi -Method 'Post' -Path '/api/orders' -Token $patientToken -Body @{ response_id = $responseId; delivery = $true }
AddResult -Endpoint 'POST /api/orders' -Result $r
if (-not $r.ok) { $results | Format-Table -Wrap -AutoSize; exit 1 }
$orderId = [int]$r.data.order.id

$r = CallApi -Method 'Get' -Path '/api/orders' -Token $patientToken
AddResult -Endpoint 'GET /api/orders (patient)' -Result $r

$r = CallApi -Method 'Get' -Path '/api/orders' -Token $pharmacyToken
AddResult -Endpoint 'GET /api/orders (pharmacy)' -Result $r

$r = CallApi -Method 'Patch' -Path '/api/orders/status' -Token $pharmacyToken -Body @{ order_id = $orderId; status = 'preparing' }
AddResult -Endpoint 'PATCH /api/orders/status' -Result $r

$r = CallApi -Method 'Get' -Path '/api/admin/users' -Token $adminToken
AddResult -Endpoint 'GET /api/admin/users' -Result $r

$r = CallApi -Method 'Get' -Path '/api/admin/orders' -Token $adminToken
AddResult -Endpoint 'GET /api/admin/orders' -Result $r

$results | Format-Table -Wrap -AutoSize

if (($results | Where-Object { -not $_.ok }).Count -gt 0) {
    exit 1
}
