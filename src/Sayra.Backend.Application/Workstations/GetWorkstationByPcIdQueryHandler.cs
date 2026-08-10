using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sayra.Backend.Application.Abstractions.Messaging;
using Sayra.Backend.Application.Abstractions.Persistence;
using Sayra.Backend.Domain;
using Sayra.Backend.Shared;

namespace Sayra.Backend.Application.Workstations
{
    public class GetWorkstationByPcIdQueryHandler : IQueryHandler<GetWorkstationByPcIdQuery, Workstation?>
    {
        private readonly IRepository<Workstation> _workstationRepository;

        public GetWorkstationByPcIdQueryHandler(IRepository<Workstation> workstationRepository)
        {
            _workstationRepository = workstationRepository;
        }

        public async Task<Result<Workstation?>> HandleAsync(GetWorkstationByPcIdQuery query, CancellationToken cancellationToken = default)
        {
            var pcIdUpper = (query.PcId ?? string.Empty).Trim().ToUpperInvariant();
            var workstations = await _workstationRepository.GetAllAsync(track: false, cancellationToken);
            var workstation = workstations.FirstOrDefault(w => w.PcId.Equals(pcIdUpper, StringComparison.OrdinalIgnoreCase));
            return Result<Workstation?>.Success(workstation);
        }
    }
}
