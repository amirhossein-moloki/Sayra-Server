using System;
using System.Collections.Generic;

namespace Sayra.Backend.Application.Configuration.Models
{
    public enum ValidationErrorSeverity
    {
        Error,
        Warning
    }

    public class ConfigurationValidationError
    {
        public string Path { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public ValidationErrorSeverity Severity { get; set; } = ValidationErrorSeverity.Error;

        public ConfigurationValidationError()
        {
        }

        public ConfigurationValidationError(string path, string code, string message, ValidationErrorSeverity severity = ValidationErrorSeverity.Error)
        {
            Path = path ?? string.Empty;
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
            Severity = severity;
        }
    }

    public class ConfigurationValidationResult
    {
        public bool IsValid => Errors.Count == 0;
        public List<ConfigurationValidationError> Errors { get; } = new List<ConfigurationValidationError>();

        public static ConfigurationValidationResult Success() => new ConfigurationValidationResult();

        public static ConfigurationValidationResult Failure(IEnumerable<ConfigurationValidationError> errors)
        {
            var result = new ConfigurationValidationResult();
            if (errors != null)
            {
                result.Errors.AddRange(errors);
            }
            return result;
        }

        public static ConfigurationValidationResult Failure(string path, string code, string message)
        {
            var result = new ConfigurationValidationResult();
            result.Errors.Add(new ConfigurationValidationError(path, code, message));
            return result;
        }

        public void AddError(string path, string code, string message, ValidationErrorSeverity severity = ValidationErrorSeverity.Error)
        {
            Errors.Add(new ConfigurationValidationError(path, code, message, severity));
        }
    }
}
