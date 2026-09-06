using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sayra.Backend.Api.Security;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Security;
using Sayra.Backend.Application.Security;
using Sayra.Backend.Application.Updates;
using Sayra.Backend.Contracts;
using Sayra.Backend.Domain;

namespace Sayra.Backend.Api.Controllers
{
    [ApiController]
    [Route("api/updates")]
    public class UpdateArtifactsController : ControllerBase
    {
        private readonly ICommandHandler<UploadUpdatePackageCommand, ClientUpdatePackageMetadataContract> _uploadHandler;
        private readonly ICommandHandler<ValidateUpdatePackageCommand, ClientUpdatePackageMetadataContract> _validateHandler;
        private readonly IQueryHandler<GetUpdatePackageQuery, ClientUpdatePackageMetadataContract> _getQueryHandler;
        private readonly ICommandHandler<DeleteUpdatePackageCommand, bool> _deleteHandler;
        private readonly ICommandHandler<SignUpdatePackageCommand, ClientUpdatePackageMetadataContract> _signHandler;
        private readonly IQueryHandler<VerifyUpdatePackageSignatureQuery, UpdateSignatureVerificationResult> _verifyHandler;

        public UpdateArtifactsController(
            ICommandHandler<UploadUpdatePackageCommand, ClientUpdatePackageMetadataContract> uploadHandler,
            ICommandHandler<ValidateUpdatePackageCommand, ClientUpdatePackageMetadataContract> validateHandler,
            IQueryHandler<GetUpdatePackageQuery, ClientUpdatePackageMetadataContract> getQueryHandler,
            ICommandHandler<DeleteUpdatePackageCommand, bool> deleteHandler,
            ICommandHandler<SignUpdatePackageCommand, ClientUpdatePackageMetadataContract> signHandler,
            IQueryHandler<VerifyUpdatePackageSignatureQuery, UpdateSignatureVerificationResult> verifyHandler)
        {
            _uploadHandler = uploadHandler ?? throw new ArgumentNullException(nameof(uploadHandler));
            _validateHandler = validateHandler ?? throw new ArgumentNullException(nameof(validateHandler));
            _getQueryHandler = getQueryHandler ?? throw new ArgumentNullException(nameof(getQueryHandler));
            _deleteHandler = deleteHandler ?? throw new ArgumentNullException(nameof(deleteHandler));
            _signHandler = signHandler ?? throw new ArgumentNullException(nameof(signHandler));
            _verifyHandler = verifyHandler ?? throw new ArgumentNullException(nameof(verifyHandler));
        }

        [HttpPost("releases/{releaseId:guid}/packages/upload")]
        [HasPermission(PermissionCatalog.ManageUpdates)]
        [RequestSizeLimit(524_288_000)] // 500 MB max upload limit
        public async Task<IActionResult> UploadPackageAsync(
            Guid releaseId,
            IFormFile file,
            [FromQuery] string? declaredSha256 = null,
            [FromQuery] UpdatePackageType packageType = UpdatePackageType.Spk,
            CancellationToken cancellationToken = default)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { code = "EMPTY_UPLOAD_STREAM", message = "File payload is missing or empty." });
            }

            var principal = GetActingPrincipal();

            using var contentStream = file.OpenReadStream();

            var command = new UploadUpdatePackageCommand
            {
                ReleaseId = releaseId,
                FileName = file.FileName,
                ContentStream = contentStream,
                DeclaredSha256 = declaredSha256,
                PackageType = packageType,
                Principal = principal
            };

            var result = await _uploadHandler.HandleAsync(command, cancellationToken);
            if (!result.IsSuccess || result.Value == null)
            {
                if (result.ErrorCode == "PERMISSION_DENIED" || result.ErrorCode == "CROSS_ORGANIZATION_ACCESS_DENIED")
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                if (result.ErrorCode == "RELEASE_NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                if (result.ErrorCode == "PACKAGE_QUARANTINED")
                {
                    return StatusCode(StatusCodes.Status422UnprocessableEntity, new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "UPLOAD_FAILED", message = result.ErrorMessage });
            }

            return Created($"/api/updates/packages/{result.Value.PackageId}", result.Value);
        }

        [HttpGet("packages/{packageId:guid}")]
        [HasPermission(PermissionCatalog.ViewUpdates)]
        public async Task<IActionResult> GetPackageMetadataAsync(Guid packageId, CancellationToken cancellationToken = default)
        {
            var principal = GetActingPrincipal();
            var query = new GetUpdatePackageQuery
            {
                PackageId = packageId,
                Principal = principal
            };

            var result = await _getQueryHandler.HandleAsync(query, cancellationToken);
            if (!result.IsSuccess || result.Value == null)
            {
                if (result.ErrorCode == "PERMISSION_DENIED" || result.ErrorCode == "CROSS_ORGANIZATION_ACCESS_DENIED")
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                if (result.ErrorCode == "PACKAGE_NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "GET_PACKAGE_FAILED", message = result.ErrorMessage });
            }

            return Ok(result.Value);
        }

        [HttpPost("packages/{packageId:guid}/validate")]
        [HasPermission(PermissionCatalog.ManageUpdates)]
        public async Task<IActionResult> ValidatePackageAsync(Guid packageId, CancellationToken cancellationToken = default)
        {
            var principal = GetActingPrincipal();
            var command = new ValidateUpdatePackageCommand
            {
                PackageId = packageId,
                Principal = principal
            };

            var result = await _validateHandler.HandleAsync(command, cancellationToken);
            if (!result.IsSuccess || result.Value == null)
            {
                if (result.ErrorCode == "PERMISSION_DENIED" || result.ErrorCode == "CROSS_ORGANIZATION_ACCESS_DENIED")
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                if (result.ErrorCode == "PACKAGE_NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "VALIDATION_FAILED", message = result.ErrorMessage });
            }

            return Ok(result.Value);
        }

        [HttpDelete("packages/{packageId:guid}")]
        [HasPermission(PermissionCatalog.ManageUpdates)]
        public async Task<IActionResult> DeletePackageAsync(Guid packageId, CancellationToken cancellationToken = default)
        {
            var principal = GetActingPrincipal();
            var command = new DeleteUpdatePackageCommand
            {
                PackageId = packageId,
                Principal = principal
            };

            var result = await _deleteHandler.HandleAsync(command, cancellationToken);
            if (!result.IsSuccess)
            {
                if (result.ErrorCode == "PERMISSION_DENIED" || result.ErrorCode == "CROSS_ORGANIZATION_ACCESS_DENIED")
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "DELETE_PACKAGE_FAILED", message = result.ErrorMessage });
            }

            if (!result.Value)
            {
                return NotFound(new { code = "PACKAGE_NOT_FOUND", message = $"Package '{packageId}' not found." });
            }

            return NoContent();
        }

        [HttpPost("packages/{packageId:guid}/sign")]
        [HasPermission(PermissionCatalog.ManageUpdates)]
        public async Task<IActionResult> SignPackageAsync(
            Guid packageId,
            [FromQuery] string? keyId = null,
            CancellationToken cancellationToken = default)
        {
            var principal = GetActingPrincipal();
            var command = new SignUpdatePackageCommand
            {
                PackageId = packageId,
                KeyId = keyId,
                Principal = principal
            };

            var result = await _signHandler.HandleAsync(command, cancellationToken);
            if (!result.IsSuccess || result.Value == null)
            {
                if (result.ErrorCode == "PERMISSION_DENIED" || result.ErrorCode == "CROSS_ORGANIZATION_ACCESS_DENIED")
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                if (result.ErrorCode == "PACKAGE_NOT_FOUND" || result.ErrorCode == "RELEASE_NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                if (result.ErrorCode == "TOCTOU_INTEGRITY_VIOLATION")
                {
                    return StatusCode(StatusCodes.Status422UnprocessableEntity, new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "SIGNING_FAILED", message = result.ErrorMessage });
            }

            return Ok(result.Value);
        }

        [HttpPost("packages/{packageId:guid}/verify")]
        [HasPermission(PermissionCatalog.ViewUpdates)]
        public async Task<IActionResult> VerifyPackageAsync(
            Guid packageId,
            CancellationToken cancellationToken = default)
        {
            var principal = GetActingPrincipal();
            var query = new VerifyUpdatePackageSignatureQuery
            {
                PackageId = packageId,
                Principal = principal
            };

            var result = await _verifyHandler.HandleAsync(query, cancellationToken);
            if (!result.IsSuccess || result.Value == null)
            {
                if (result.ErrorCode == "PERMISSION_DENIED" || result.ErrorCode == "CROSS_ORGANIZATION_ACCESS_DENIED")
                {
                    return StatusCode(StatusCodes.Status403Forbidden, new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                if (result.ErrorCode == "PACKAGE_NOT_FOUND" || result.ErrorCode == "RELEASE_NOT_FOUND")
                {
                    return NotFound(new { code = result.ErrorCode, message = result.ErrorMessage });
                }

                return BadRequest(new { code = result.ErrorCode ?? "VERIFICATION_FAILED", message = result.ErrorMessage });
            }

            return Ok(result.Value);
        }

        private UserPrincipal GetActingPrincipal()
        {
            return HttpContext.Items["UserPrincipal"] as UserPrincipal ?? UserPrincipal.Anonymous;
        }
    }
}
