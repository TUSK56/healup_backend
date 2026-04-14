using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Configuration;

namespace HealUp.Api.Services;

public class CloudinaryService
{
    private readonly IConfiguration _configuration;

    public CloudinaryService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<string> UploadPrescriptionAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        var cloudName = _configuration["Cloudinary:CloudName"];
        var apiKey = _configuration["Cloudinary:ApiKey"];
        var apiSecret = _configuration["Cloudinary:ApiSecret"];

        if (string.IsNullOrWhiteSpace(cloudName) || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
        {
            throw new InvalidOperationException("HealUp: Cloudinary is not configured. Set Cloudinary credentials before uploading prescriptions.");
        }

        var account = new Account(cloudName, apiKey, apiSecret);
        var cloudinary = new Cloudinary(account) { Api = { Secure = true } };

        await using var stream = file.OpenReadStream();

        var uploadParams = new ImageUploadParams
        {
            File = new FileDescription(file.FileName, stream),
            Folder = "healup/prescriptions"
        };

        var result = await cloudinary.UploadAsync(uploadParams, cancellationToken);
        if (result.Error != null)
        {
            throw new InvalidOperationException($"HealUp: Cloudinary upload failed - {result.Error.Message}");
        }

        return result.SecureUrl?.ToString() ?? result.Url?.ToString() ?? string.Empty;
    }
}

