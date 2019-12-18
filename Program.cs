using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Transactions;
using Devart.Data.Oracle;
using HibernatingRhinos.Profiler.Appender.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace devart_efcore_3_value_conversion_bug
{
    public class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
                EntityFrameworkProfiler.Initialize();

                var config = Devart.Data.Oracle.Entity.Configuration.OracleEntityProviderConfig.Instance;
                config.CodeFirstOptions.UseNonUnicodeStrings = true;
                config.CodeFirstOptions.UseNonLobStrings = true;

                var builder = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.development.json", optional: false, reloadOnChange: true);
                var configuration = builder.Build();
                EntityContext.ConnectionString = ComposeConnectionString(configuration);

                using (var scope = new TransactionScope(
                    TransactionScopeOption.Required,
                    new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted },
                    TransactionScopeAsyncFlowOption.Enabled))
                {
                    using (var context = new EntityContext())
                    {
                        context.Database.EnsureDeleted();

                        context.Database.ExecuteSqlRaw(@"
CREATE TABLE BEAST_RIDER
(
    ID          NUMBER (19, 0) GENERATED ALWAYS AS IDENTITY NOT NULL,
    RIDER_NAME        VARCHAR2 (50 CHAR) NOT NULL,
    BEAST_TYPE        VARCHAR2 (50 CHAR) NOT NULL
)");
                        context.Database.ExecuteSqlRaw(@"INSERT INTO BEAST_RIDER (RIDER_NAME, BEAST_TYPE) VALUES ('Khal Drogo', 'Unicorn')");

                        await context.SaveChangesAsync();
                    }

                    scope.Complete();
                }

                await using (var context = new EntityContext())
                {
                    // Throws System.InvalidOperationException:
                    // No coercion operator is defined between types 'System.String' and 'devart_efcore_3_value_conversion_bug.EquineBeast'.
                    var unicornRiders = await context.Set<BeastRider>()
                        .Where(_ => _.Beast == EquineBeast.Unicorn)
                        .ToArrayAsync();

                    Console.WriteLine($"Found {unicornRiders.Length} unicorn riders.");
                }

                Console.WriteLine("Finished.");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
            }

            Console.ReadKey();
        }

        private static string ComposeConnectionString(IConfiguration configuration)
        {
            var builder = new OracleConnectionStringBuilder
            {
                Server = configuration["DatabaseServer"],
                UserId = configuration["UserId"],
                Password = configuration["Password"],
                ServiceName = configuration["ServiceName"],
                Port = int.Parse(configuration["Port"]),
                Direct = true,
                Pooling = true,
                LicenseKey = configuration["DevartLicenseKey"]
            };
            return builder.ToString();
        }
    }

    public class EntityContext : DbContext
    {
        public static string ConnectionString;

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
            optionsBuilder.UseOracle(ConnectionString);

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<BeastRider>().ToTable("BEAST_RIDER");
            modelBuilder.Entity<BeastRider>().HasKey(_ => _.Id);
            modelBuilder.Entity<BeastRider>().Property(_ => _.Id).HasColumnName("ID");
            modelBuilder.Entity<BeastRider>().Property(_ => _.RiderName).HasColumnName("RIDER_NAME");
            modelBuilder.Entity<BeastRider>().Property(_ => _.Beast).HasColumnName("BEAST_TYPE");

            // Does not work
            modelBuilder.Entity<BeastRider>().Property(_ => _.Beast).HasConversion<string>();

            // Works
            //var converter = new EnumToStringConverter<EquineBeast>();
            //modelBuilder.Entity<BeastRider>().Property(_ => _.Beast).HasConversion(converter);
        }
    }

    public class BeastRider
    {
        public long Id { get; private set; }

        public string RiderName { get; private set; }

        public EquineBeast Beast { get; private set; }
        
        public BeastRider()
        {
            // Required by EF Core
        }

        public BeastRider(string riderName, EquineBeast beast)
        {
            RiderName = riderName;
            Beast = beast;
        }
    }

    public enum EquineBeast
    {
        Donkey,
        Mule,
        Horse,
        Unicorn
    }
}
