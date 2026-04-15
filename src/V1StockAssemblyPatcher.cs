#nullable enable

using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using System;
using System.IO;
using System.Linq;

internal static class V1StockAssemblyPatcher
{
    private const string HookAssemblyName = "CloneHeroV1StockTracker";
    private const string HookTypeName = "CloneHeroSectionTracker.V1Stock.StockTrackerHooks";
    private const string UpdateHookMethodName = "OnGameManagerUpdate";
    private const string MainMenuHookMethodName = "OnMainMenuUpdate";
    private const string NoteMissHookMethodName = "OnBasePlayerNoteMiss";

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: V1StockAssemblyPatcher <Assembly-CSharp.dll> <CloneHeroV1StockTracker.dll>");
            return 2;
        }

        string assemblyPath = Path.GetFullPath(args[0]);
        string hookAssemblyPath = Path.GetFullPath(args[1]);
        if (!File.Exists(assemblyPath))
        {
            Console.Error.WriteLine($"Missing assembly: {assemblyPath}");
            return 1;
        }

        if (!File.Exists(hookAssemblyPath))
        {
            Console.Error.WriteLine($"Missing hook assembly: {hookAssemblyPath}");
            return 1;
        }

        string backupPath = assemblyPath + ".stocktracker.bak";
        if (!File.Exists(backupPath))
        {
            File.Copy(assemblyPath, backupPath, overwrite: false);
        }

        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(assemblyPath)!);
        resolver.AddSearchDirectory(Path.GetDirectoryName(hookAssemblyPath)!);

        using AssemblyDefinition targetAssembly = AssemblyDefinition.ReadAssembly(
            assemblyPath,
            new ReaderParameters
            {
                AssemblyResolver = resolver,
                InMemory = true
            });
        using AssemblyDefinition hookAssembly = AssemblyDefinition.ReadAssembly(
            hookAssemblyPath,
            new ReaderParameters { AssemblyResolver = resolver });

        ModuleDefinition targetModule = targetAssembly.MainModule;
        TypeDefinition? gameManager = targetModule.Types.FirstOrDefault(type => type.Name == "GameManager");
        if (gameManager == null)
        {
            Console.Error.WriteLine("GameManager type not found.");
            return 1;
        }

        MethodDefinition? updateMethod = gameManager.Methods.FirstOrDefault(method => method.Name == "Update" && !method.HasParameters);
        if (updateMethod == null || !updateMethod.HasBody)
        {
            Console.Error.WriteLine("GameManager.Update() not found.");
            return 1;
        }

        if (!targetModule.AssemblyReferences.Any(reference => reference.Name == HookAssemblyName))
        {
            targetModule.AssemblyReferences.Add(new AssemblyNameReference(HookAssemblyName, new Version(1, 0, 0, 0)));
        }

        TypeDefinition? hookType = hookAssembly.MainModule.Types.FirstOrDefault(type => type.FullName == HookTypeName);
        MethodDefinition? updateHookMethod = hookType?.Methods.FirstOrDefault(method => method.Name == UpdateHookMethodName && method.Parameters.Count == 1);
        MethodDefinition? mainMenuHookMethod = hookType?.Methods.FirstOrDefault(method => method.Name == MainMenuHookMethodName && method.Parameters.Count == 1);
        MethodDefinition? noteMissHookMethod = hookType?.Methods.FirstOrDefault(method => method.Name == NoteMissHookMethodName && method.Parameters.Count == 2);
        if (updateHookMethod == null || mainMenuHookMethod == null || noteMissHookMethod == null)
        {
            Console.Error.WriteLine("One or more hook methods were not found in the helper assembly.");
            return 1;
        }

        int patchesApplied = 0;
        MethodReference importedUpdateHook = targetModule.ImportReference(updateHookMethod);
        if (!HasHookCall(updateMethod, UpdateHookMethodName))
        {
            InsertSingleArgHook(updateMethod, importedUpdateHook);
            patchesApplied++;
        }

        TypeDefinition? mainMenu = targetModule.Types.FirstOrDefault(type => type.Name == "MainMenu");
        if (mainMenu == null)
        {
            Console.Error.WriteLine("MainMenu type not found.");
            return 1;
        }

        MethodDefinition? mainMenuUpdateMethod = mainMenu.Methods.FirstOrDefault(method => method.Name == "Update" && !method.HasParameters);
        if (mainMenuUpdateMethod == null || !mainMenuUpdateMethod.HasBody)
        {
            Console.Error.WriteLine("MainMenu.Update() not found.");
            return 1;
        }

        MethodReference importedMainMenuHook = targetModule.ImportReference(mainMenuHookMethod);
        if (!HasHookCall(mainMenuUpdateMethod, MainMenuHookMethodName))
        {
            InsertSingleArgHook(mainMenuUpdateMethod, importedMainMenuHook);
            patchesApplied++;
        }

        TypeDefinition? basePlayer = targetModule.Types.FirstOrDefault(type => type.Name == "BasePlayer");
        if (basePlayer == null)
        {
            Console.Error.WriteLine("BasePlayer type not found.");
            return 1;
        }

        MethodDefinition? noteMissMethod = basePlayer.Methods.FirstOrDefault(method => HasHookCall(method, NoteMissHookMethodName))
            ?? basePlayer.Methods.FirstOrDefault(IsBasePlayerMissMethod);
        if (noteMissMethod == null)
        {
            Console.Error.WriteLine("BasePlayer miss method not found.");
            return 1;
        }

        MethodReference importedNoteMissHook = targetModule.ImportReference(noteMissHookMethod);
        if (!HasHookCall(noteMissMethod, NoteMissHookMethodName))
        {
            InsertTwoArgHook(noteMissMethod, importedNoteMissHook);
            patchesApplied++;
        }

        if (patchesApplied == 0)
        {
            Console.WriteLine("Assembly already patched.");
            return 0;
        }

        targetAssembly.Write(assemblyPath);
        Console.WriteLine("Patched Assembly-CSharp.dll successfully.");
        return 0;
    }

    private static void InsertSingleArgHook(MethodDefinition method, MethodReference hook)
    {
        ILProcessor il = method.Body.GetILProcessor();
        Instruction first = method.Body.Instructions[0];
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Call, hook));
        method.Body.SimplifyMacros();
        method.Body.OptimizeMacros();
    }

    private static void InsertTwoArgHook(MethodDefinition method, MethodReference hook)
    {
        ILProcessor il = method.Body.GetILProcessor();
        Instruction first = method.Body.Instructions[0];
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_1));
        il.InsertBefore(first, il.Create(OpCodes.Call, hook));
        method.Body.SimplifyMacros();
        method.Body.OptimizeMacros();
    }

    private static bool HasHookCall(MethodDefinition method, string hookMethodName)
    {
        if (method == null || !method.HasBody)
        {
            return false;
        }

        return method.Body.Instructions.Any(instruction => IsHookCall(instruction, hookMethodName));
    }

    private static bool IsHookCall(Instruction instruction, string hookMethodName)
    {
        if (instruction.OpCode != OpCodes.Call || instruction.Operand is not MethodReference calledMethod)
        {
            return false;
        }

        return calledMethod.Name == hookMethodName && calledMethod.DeclaringType.FullName == HookTypeName;
    }

    private static bool IsBasePlayerMissMethod(MethodDefinition method)
    {
        if (!method.HasBody || method.Parameters.Count != 1)
        {
            return false;
        }

        var instructions = method.Body.Instructions;
        if (instructions.Count < 6)
        {
            return false;
        }

        // Stock v1 miss path starts by marking the note missed:
        // ldarg.1, ldc.i4.1, stfld <note-bool-field>, ldarg.1, callvirt <note-bool-getter>, brfalse.s/ret
        return instructions[0].OpCode == OpCodes.Ldarg_1 &&
            instructions[1].OpCode == OpCodes.Ldc_I4_1 &&
            instructions[2].OpCode == OpCodes.Stfld &&
            instructions[3].OpCode == OpCodes.Ldarg_1 &&
            instructions[4].OpCode == OpCodes.Callvirt;
    }
}
