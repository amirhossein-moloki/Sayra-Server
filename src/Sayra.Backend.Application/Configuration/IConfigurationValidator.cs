using Sayra.Backend.Application.Configuration.Models;

namespace Sayra.Backend.Application.Configuration
{
    public interface IConfigurationValidator
    {
        ConfigurationValidationResult Validate(string rawJsonPayload);
        ConfigurationValidationResult Validate(SayraConfigurationSchema schemaModel);
    }
}
