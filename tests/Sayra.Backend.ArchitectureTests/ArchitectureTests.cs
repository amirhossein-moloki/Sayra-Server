using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Sayra.Backend.ArchitectureTests
{
    public class ArchitectureTests
    {
        private static readonly Assembly DomainAssembly = typeof(Domain.BaseEntity).Assembly;
        private static readonly Assembly ApplicationAssembly = typeof(Application.Abstractions.Persistence.IUnitOfWork).Assembly;
        private static readonly Assembly SharedAssembly = typeof(Shared.Money).Assembly;

        [Fact]
        public void Domain_Should_Not_Have_Dependency_On_Other_Projects()
        {
            // Domain can only depend on Shared and System/Microsoft namespaces
            var referencedAssemblies = DomainAssembly.GetReferencedAssemblies();

            foreach (var assembly in referencedAssemblies)
            {
                var name = assembly.Name;
                if (name != null && name.StartsWith("Sayra") && !name.Contains("Shared"))
                {
                    Assert.Fail($"Domain layer cannot depend on {name}");
                }
            }
        }

        [Fact]
        public void Application_Should_Not_Have_Dependency_On_Infrastructure_Or_Api()
        {
            var referencedAssemblies = ApplicationAssembly.GetReferencedAssemblies();

            foreach (var assembly in referencedAssemblies)
            {
                var name = assembly.Name;
                if (name != null && (name.Contains("Infrastructure") || name.Contains("Api")))
                {
                    Assert.Fail($"Application layer cannot depend on {name}");
                }
            }
        }

        [Fact]
        public void Modules_Should_Only_Depend_On_Core_Layers_And_Not_Cross_Reference_Directly()
        {
            // Let's test standard module assemblies
            var moduleNames = new[] { "Workstations", "Sessions", "Authentication", "Configuration", "Updates", "Telemetry", "Events", "Commands", "Fleet" };

            foreach (var moduleName in moduleNames)
            {
                try
                {
                    var assembly = Assembly.Load($"Sayra.Backend.Modules.{moduleName}");
                    var referenced = assembly.GetReferencedAssemblies();

                    foreach (var refAssembly in referenced)
                    {
                        var name = refAssembly.Name;
                        if (name != null && name.Contains("Modules") && !name.EndsWith(moduleName))
                        {
                            Assert.Fail($"Module {moduleName} directly depends on another module: {name}");
                        }
                        if (name != null && (name.Contains("Infrastructure") || name.Contains("Api")))
                        {
                            Assert.Fail($"Module {moduleName} directly depends on concrete layer: {name}");
                        }
                    }
                }
                catch (System.IO.FileNotFoundException)
                {
                    // If a module assembly is named differently (e.g., matching the folder name)
                    var assembly = Assembly.Load(moduleName);
                    var referenced = assembly.GetReferencedAssemblies();

                    foreach (var refAssembly in referenced)
                    {
                        var name = refAssembly.Name;
                        // It shouldn't depend on other modules
                        if (name != null && moduleNames.Contains(name) && name != moduleName)
                        {
                            Assert.Fail($"Module {moduleName} directly depends on another module: {name}");
                        }
                        if (name != null && (name.Contains("Infrastructure") || name.Contains("Api")))
                        {
                            Assert.Fail($"Module {moduleName} directly depends on concrete layer: {name}");
                        }
                    }
                }
            }
        }
    }
}
