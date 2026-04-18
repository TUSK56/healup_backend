$loginUrl = "http://127.0.0.1:8000/api/login"
$loginBody = @{
    email = "patient1@demo.healup.local"
    password = "Demo@2026"
    guard = "patient"
} | ConvertTo-Json

$headers = @{"Content-Type" = "application/json"}

try {
    $loginResponse = Invoke-RestMethod -Uri $loginUrl -Method Post -Body $loginBody -Headers $headers -ErrorAction Stop
    Write-Host "Login Status: 200 (Success)"
    Write-Host "Login Response Content: $($loginResponse | ConvertTo-Json -Depth 5)"

    $token = $null
    if ($loginResponse.token) { $token = $loginResponse.token }
    elseif ($loginResponse.data -and $loginResponse.data.token) { $token = $loginResponse.data.token }

    if ($token) {
        Write-Host "Token found. Proceeding to add address..."
        
        $addressUrl = "http://127.0.0.1:8000/api/patient/addresses"
        $addressBody = @{
            label = "Home Test"
            iconKey = "home"
            city = "Cairo"
            district = "Nasr City"
            addressDetails = "Street 1, Building 2"
            latitude = 30.05
            longitude = 31.24
        } | ConvertTo-Json

        $authHeaders = @{
            "Authorization" = "Bearer $token"
            "Content-Type"  = "application/json"
            "Accept"        = "application/json"
        }

        try {
            $addrResponse = Invoke-RestMethod -Uri $addressUrl -Method Post -Body $addressBody -Headers $authHeaders -ErrorAction Stop
            Write-Host "Add Address Status: 200 (Success)"
            Write-Host "Add Address Response Content: $($addrResponse | ConvertTo-Json -Depth 5)"
        } catch {
            Write-Host "Add Address failed."
            if ($_.Exception.Response) {
                $statusCode = [int]$_.Exception.Response.StatusCode
                Write-Host "Status Code: $statusCode"
                $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
                $body = $reader.ReadToEnd()
                Write-Host "Error Body: $body"
            } else {
                Write-Host "Error: $($_.Exception.Message)"
            }
        }
    } else {
        Write-Host "Login successful but no token found in response."
    }
} catch {
    Write-Host "Login request failed."
    if ($_.Exception.Response) {
        $statusCode = [int]$_.Exception.Response.StatusCode
        Write-Host "Status Code: $statusCode"
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $body = $reader.ReadToEnd()
        Write-Host "Error Body: $body"
    } else {
        Write-Host "Error: $($_.Exception.Message)"
    }
}
