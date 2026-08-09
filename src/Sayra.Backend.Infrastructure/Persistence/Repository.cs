using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Sayra.Backend.Domain;
using Sayra.Backend.Application.Abstractions.Persistence;

namespace Sayra.Backend.Infrastructure.Persistence
{
    public class Repository<T> : IRepository<T> where T : BaseEntity
    {
        protected readonly DbContext _context;
        protected readonly DbSet<T> _dbSet;

        public Repository(DbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _dbSet = _context.Set<T>();
        }

        public virtual async Task<T?> GetByIdAsync(Guid id, bool track = true, CancellationToken cancellationToken = default)
        {
            if (track)
            {
                return await _dbSet.FindAsync(new object[] { id }, cancellationToken);
            }
            else
            {
                return await _dbSet.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
            }
        }

        public virtual async Task<IReadOnlyList<T>> GetAllAsync(bool track = true, CancellationToken cancellationToken = default)
        {
            if (track)
            {
                return await _dbSet.ToListAsync(cancellationToken);
            }
            else
            {
                return await _dbSet.AsNoTracking().ToListAsync(cancellationToken);
            }
        }

        public virtual async Task AddAsync(T entity, CancellationToken cancellationToken = default)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            await _dbSet.AddAsync(entity, cancellationToken);
        }

        public virtual void Update(T entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            _dbSet.Update(entity);
        }

        public virtual void Delete(T entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            _dbSet.Remove(entity);
        }
    }
}
