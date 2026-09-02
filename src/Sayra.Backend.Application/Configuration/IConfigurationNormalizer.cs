using Sayra.Backend.Application.Configuration.Models;

namespace Sayra.Backend.Application.Configuration
{
    public interface IConfigurationNormalizer
    {
        string NormalizeToJson(string rawJsonPayload);
        string NormalizeToJson(SayraConfigurationSchema schemaModel);
        SayraConfigurationSchema NormalizeToModel(SayraConfigurationSchema schemaModel);
    }
}
