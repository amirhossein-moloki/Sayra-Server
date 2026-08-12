using System;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Security;

namespace Sayra.Backend.Application.Security
{
    public interface ISecureMessageService
    {
        SecureMessageEnvelope EncryptAndSign(object payload, byte[] sessionKey);
        SecureMessageValidationResult DecryptAndVerify(SecureMessageEnvelope envelope, byte[] sessionKey);
        Task SendSecureMessageAsync(ConnectionSession session, object payload);
        Task<SecureMessageValidationResult> HandleSecureMessageAsync(ConnectionSession session, SecureMessageEnvelope envelope);
    }
}
