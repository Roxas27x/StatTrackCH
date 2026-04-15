#nullable enable

using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: V1RuntimeCompatibilityChecker <helper-assembly> <managed-dir>");
            return 2;
        }

        string helperAssemblyPath = args[0];
        string managedDir = args[1];
        if (!File.Exists(helperAssemblyPath))
        {
            Console.Error.WriteLine("Missing helper assembly: " + helperAssemblyPath);
            return 2;
        }

        if (!Directory.Exists(managedDir))
        {
            Console.Error.WriteLine("Missing managed directory: " + managedDir);
            return 2;
        }

        try
        {
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(managedDir);
            resolver.AddSearchDirectory(Path.GetDirectoryName(helperAssemblyPath) ?? managedDir);

            var readerParameters = new ReaderParameters
            {
                AssemblyResolver = resolver,
                InMemory = true
            };

            AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(helperAssemblyPath, readerParameters);
            var failures = new SortedSet<string>(StringComparer.Ordinal);

            foreach (TypeDefinition type in EnumerateTypes(assembly.MainModule.Types))
            {
                foreach (MethodDefinition method in type.Methods)
                {
                    if (!method.HasBody)
                    {
                        continue;
                    }

                    foreach (Instruction instruction in method.Body.Instructions)
                    {
                        if (instruction.Operand is MethodReference methodReference)
                        {
                            ValidateMethod(methodReference, failures);
                            continue;
                        }

                        if (instruction.Operand is FieldReference fieldReference)
                        {
                            ValidateField(fieldReference, failures);
                        }
                    }
                }
            }

            if (failures.Count == 0)
            {
                Console.WriteLine("Runtime compatibility check passed.");
                return 0;
            }

            Console.Error.WriteLine("Runtime compatibility check failed. Unresolved members:");
            foreach (string failure in failures)
            {
                Console.Error.WriteLine(" - " + failure);
            }

            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static IEnumerable<TypeDefinition> EnumerateTypes(IEnumerable<TypeDefinition> types)
    {
        foreach (TypeDefinition type in types)
        {
            yield return type;
            foreach (TypeDefinition nested in EnumerateTypes(type.NestedTypes))
            {
                yield return nested;
            }
        }
    }

    private static void ValidateMethod(MethodReference methodReference, ISet<string> failures)
    {
        if (!ShouldCheck(methodReference.DeclaringType))
        {
            return;
        }

        try
        {
            if (methodReference.Resolve() == null)
            {
                failures.Add("method: " + methodReference.FullName);
            }
        }
        catch (Exception ex)
        {
            failures.Add("method: " + methodReference.FullName + " | " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static void ValidateField(FieldReference fieldReference, ISet<string> failures)
    {
        if (!ShouldCheck(fieldReference.DeclaringType))
        {
            return;
        }

        try
        {
            if (fieldReference.Resolve() == null)
            {
                failures.Add("field: " + fieldReference.FullName);
            }
        }
        catch (Exception ex)
        {
            failures.Add("field: " + fieldReference.FullName + " | " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    private static bool ShouldCheck(TypeReference? typeReference)
    {
        if (typeReference == null)
        {
            return false;
        }

        AssemblyNameReference? assemblyName = GetAssemblyName(typeReference);
        if (assemblyName == null)
        {
            return false;
        }

        string name = assemblyName.Name ?? string.Empty;
        return !string.Equals(name, "CloneHeroV1StockTracker", StringComparison.OrdinalIgnoreCase);
    }

    private static AssemblyNameReference? GetAssemblyName(TypeReference typeReference)
    {
        TypeReference current = typeReference;
        while (current is TypeSpecification specification)
        {
            current = specification.ElementType;
        }

        if (current.Scope is AssemblyNameReference assemblyName)
        {
            return assemblyName;
        }

        if (current.Scope is ModuleDefinition module && module.Assembly != null)
        {
            return module.Assembly.Name;
        }

        return null;
    }
}
