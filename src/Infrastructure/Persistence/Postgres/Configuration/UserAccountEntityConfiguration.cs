using Infrastructure.Persistence.Postgres.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence.Postgres.Configuration;

public static class UserAccountEntityConfiguration
{
    public static ModelBuilder Apply(ModelBuilder model)
    {
        model.Entity<UserAccountEntity>(e =>
        {
            e.ToTable("user_accounts");
            e.HasKey(x => x.UsernameKey);
            e.Property(x => x.UsernameKey).HasMaxLength(64);
            e.Property(x => x.Username).HasMaxLength(64);
            e.Property(x => x.DisplayName).HasMaxLength(128);
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.PasswordHash).HasMaxLength(256);
            e.Property(x => x.ApiKeyHash).HasMaxLength(256);
            e.HasIndex(x => x.ApiKeyHash);
            e.Property(x => x.Version).IsConcurrencyToken();
        });
        return model;
    }
}
