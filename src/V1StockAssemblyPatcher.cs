#nullable enable

using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal static class V1StockAssemblyPatcher
{
    private const string HookAssemblyName = "StatTrack";
    private const string LegacyHookAssemblyName = "CloneHeroV1StockTracker";
    private const string HookTypeName = "CloneHeroSectionTracker.V1Stock.StockTrackerHooks";
    private const string UpdateHookMethodName = "OnGameManagerUpdate";
    private const string MainMenuHookMethodName = "OnMainMenuUpdate";
    private const string MainMenuEnableHookMethodName = "OnMainMenuEnable";
    private const string MainMenuInputBlockHookMethodName = "ShouldBlockMainMenuInput";
    private const string SongSelectHookMethodName = "OnSongSelectUpdate";
    private const string NoteMissHookMethodName = "OnBasePlayerNoteMiss";
    private const string CustomTagHelperMethodName = "ApplyCustomMainMenuTags";
    private const string VersionReplacementText =
        "StatTrack v1.0.7\n" +
        "<size=90%>Mod by Roxas27x</size>\n" +
        "<size=85%>Home / F8 to open the overlay</size>";

    private static readonly string[][] SubtitleFieldNameGroups =
    {
        new[] { "ˀˀʶʽʾˁʳʿʾʳˁ", "Ë€Ë€Ê¶Ê½Ê¾ËÊ³Ê¿Ê¾Ê³Ë", "buildTags" },
        new[] { "ʴʶʿʸʳʵʿʹˁʼʶ", "Ê´Ê¶Ê¿Ê¸Ê³ÊµÊ¿Ê¹ËÊ¼Ê¶" },
        new[] { "ʵʷʵʴʻʴʵʴˁʶʾ", "ÊµÊ·ÊµÊ´Ê»Ê´ÊµÊ´ËÊ¶Ê¾" }
    };

    private static readonly string[] CustomTags =
    {
        "1v1 me bro",
        "Daniel Naroditsky is a fucking prophet...",
        "<size=15>wydkakashi is preggers, and im not the father...</size>",
        "ARE YOU STILL DOING YOUR PART?",
        "IMAGINE THE NERVES?",
        "EVER WANTED TO SEE YOUR FRIENDS CHOKE A SOLO? NOW YOU CAN!",
        "IT'S BEEN FUN, BUT WAIT UNTIL YOU SEE THE FUTURE,",
        "NOW WITH INPUT SWAPPING PAUSE DISABLED, IF YOU REALLY WANNA",
        "oos goos...",
        "DO YOU NEED SOME PRECISION DISH DETERGENT FOR THOSE PLATES?",
        "oos.....",
        "I'M MAKING THE DUMBEST MISTAKES!!!!!",
        "MONIKA.CHR DELETED SUCCESSFULLY",
        "WE DIDN'T FORGOT WALUIGI",
        "NEW FUNKY MODE!",
        "FEATURING DANTE FROM THE DEVIL MAY CRY SERIES",
        "KNUCKLES",
        "PET YOUR DOG",
        "wait are you grinding MWW or dick's mine",
        "DO I LIKE FROMUNDA CHEESE? YEAH I LIKE FROMUNDA CHEESE; CHEESE FROMUNDA MY BALLS! -CarnyJared",
        "HI Twitch!",
        "CAN I JUST GO TO SLEEP?",
        "SPOILER: DON'T ACTUALLY DO THIS",
        "YOU WANNA HEAR OUR NEW MIXTAPE?",
        "I'm awesome that you're having a great time.",
        "WHY ARE YOU STILL HERE?",
        "I got your douchebag right here \U0001F595\U0001F3FB \U0001F595\U0001F3FB \U0001F595\U0001F3FB you F*S%R...",
        "NOT AFFILIATED WITH STAGE TOUR",
        "IS THIS EVEN REAL?",
        "a winner is a loser who gave it one more try",
        "were wit da vipers were wit da vipers",
        "INITIATING STEP ONE OF COMPLETE GLOBAL SATURATION",
        "SHOULDA SEEN THE PRICKS FACE WHEN HE SAW DA GYATTT",
        "LIKE, COMMENT AND SUBSCRIBE",
        "DISCOUNT CODE CARNY",
        "Did you hear about the new rhythm game called stage 4",
        "WELCOME TO THE CLON",
        "GETTING YOU CLOSER TO FUTURE ARTHRITIS",
        "you are what you fc",
        "How are you liking the show?",
        "\"ass any day of the week\" -schmooeyy",
        "RE-INVENTING THE WIBBLE",
        "RECORD BREAKING AWESOMENESS",
        "EVERYTHING YOU NEVER WANTED",
        "LYRICALLY INSANE",
        "\"I WISH SOMEBODY WOULD MAKE A MOD\" ROXAS:",
        "STAIRWAY TO SEVEN",
        "I CAN'T BELIEVE IT'S NOT A BITCOIN MINER",
        "DID I JOIN?",
        "TAP NOTE 4... OR ELSE",
        "COMPLETE GLOBAL SATURATION",
        "LESS STUTTER, MORE BUTTER",
        "how bout every time i land on one of my properties i get a hundred dollars. habadat?",
        "If you people really think this is real, I have some ocean front property in Arizona for sale.",
        "SAVED BY THE SCORE",
        "IS THAT PRACTICE MODE?",
        "LET'S GET SHAKIN'!",
        "SOLOS?! SOLOS!",
        "I just successfully pissed off a shit stain that's been on the toilet bowl for 2 days",
        "BREWED WITH LOVE",
        "C#2 C#3 B1 B2 A1 A2 F#1 F#2 C#2 C#3 B1 B2 A1 A2 F#1 F#2 C#2 C#3 B1 B2 A1 A2 F#1 F#2 C#2 C#3 B1 B2 A1 A2 F#1 F#2 C#2 C#3 B1 B2 A1 A2 F#1 F#2 C#2 C#3 B1 B2 A1 A2 F#1 F#2 C#2 C#3 B1 B2 A1 A2 F#1 F#2 C#2 C#3 B1 B2 A1 A2 F#1 F#2 C#2 C#3 B1 B2 A1 A2 F#1 F#2",
        "PRESS START TO PLAY",
        "WHAT WE PLAYIN' BOYS?!",
        "LET'S TRY THIS AGAIN, SHALL WE?",
        "CACHE NOT AS HUNGRY ANYMORE",
        "LETTING YOU IN SINCE NEVER",
        "DONT VACUUM",
        "THIS BUG WANTS TO TELL YOU A STORY, ALL ABOUT HOW ITS LIFE GOT FLIPPED TURNED UPSIDE DOWN",
        "WE HAVE BUGS! 50% OFF YEAR-ROUND!",
        "Wanna become famous? Buy followers, primes and viewers on twitch.tv/schmooeyy",
        "Wanna become famous? Buy followers, primes and viewers on twitch.tv/carnyjared",
        "born to shit, forced to wipe",
        "new year new me (alcoholic)",
        "I THINK WE NEED A NEW EXTERMINATOR..."
    };

    private static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine("Usage: V1StockAssemblyPatcher <Assembly-CSharp.dll> <StatTrack.dll>");
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
        MethodDefinition? mainMenuEnableHookMethod = hookType?.Methods.FirstOrDefault(method => method.Name == MainMenuEnableHookMethodName && method.Parameters.Count == 1);
        MethodDefinition? mainMenuInputBlockHookMethod = hookType?.Methods.FirstOrDefault(method => method.Name == MainMenuInputBlockHookMethodName && method.Parameters.Count == 1 && method.ReturnType.FullName == "System.Boolean");
        MethodDefinition? songSelectHookMethod = hookType?.Methods.FirstOrDefault(method => method.Name == SongSelectHookMethodName && method.Parameters.Count == 1);
        MethodDefinition? noteMissHookMethod = hookType?.Methods.FirstOrDefault(method => method.Name == NoteMissHookMethodName && method.Parameters.Count == 2);
        if (updateHookMethod == null ||
            mainMenuHookMethod == null ||
            mainMenuEnableHookMethod == null ||
            mainMenuInputBlockHookMethod == null ||
            songSelectHookMethod == null ||
            noteMissHookMethod == null)
        {
            Console.Error.WriteLine("One or more hook methods were not found in the helper assembly.");
            return 1;
        }

        int patchesApplied = 0;
        MethodReference importedUpdateHook = targetModule.ImportReference(updateHookMethod);
        if (RewriteLegacyHookCalls(updateMethod, UpdateHookMethodName, importedUpdateHook))
        {
            patchesApplied++;
        }

        if (!HasDesiredHookCall(updateMethod, UpdateHookMethodName))
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

        MethodDefinition? mainMenuOnEnableMethod = mainMenu.Methods.FirstOrDefault(method => method.Name == "OnEnable" && !method.HasParameters);
        if (mainMenuOnEnableMethod == null || !mainMenuOnEnableMethod.HasBody)
        {
            Console.Error.WriteLine("MainMenu.OnEnable() not found.");
            return 1;
        }

        MethodReference importedMainMenuHook = targetModule.ImportReference(mainMenuHookMethod);
        MethodReference importedMainMenuEnableHook = targetModule.ImportReference(mainMenuEnableHookMethod);
        MethodReference importedMainMenuInputBlockHook = targetModule.ImportReference(mainMenuInputBlockHookMethod);
        if (RewriteLegacyHookCalls(mainMenuUpdateMethod, MainMenuHookMethodName, importedMainMenuHook))
        {
            patchesApplied++;
        }

        if (!HasDesiredHookCall(mainMenuUpdateMethod, MainMenuHookMethodName))
        {
            InsertSingleArgHook(mainMenuUpdateMethod, importedMainMenuHook);
            patchesApplied++;
        }

        if (!HasDesiredHookCall(mainMenuUpdateMethod, MainMenuInputBlockHookMethodName))
        {
            InsertSingleArgReturnIfTrueHook(mainMenuUpdateMethod, importedMainMenuInputBlockHook);
            patchesApplied++;
        }

        if (!HasDesiredHookCall(mainMenuOnEnableMethod, MainMenuEnableHookMethodName))
        {
            InsertMainMenuEnableHook(mainMenuOnEnableMethod, importedMainMenuEnableHook);
            patchesApplied++;
        }

        TypeDefinition? songSelect = targetModule.Types.FirstOrDefault(type => type.Name == "SongSelect");
        if (songSelect == null)
        {
            Console.Error.WriteLine("SongSelect type not found.");
            return 1;
        }

        MethodDefinition? songSelectUpdateMethod = songSelect.Methods.FirstOrDefault(method => method.Name == "Update" && !method.HasParameters);
        if (songSelectUpdateMethod == null || !songSelectUpdateMethod.HasBody)
        {
            Console.Error.WriteLine("SongSelect.Update() not found.");
            return 1;
        }

        MethodReference importedSongSelectHook = targetModule.ImportReference(songSelectHookMethod);
        if (RewriteLegacyHookCalls(songSelectUpdateMethod, SongSelectHookMethodName, importedSongSelectHook))
        {
            patchesApplied++;
        }

        if (!HasDesiredHookCall(songSelectUpdateMethod, SongSelectHookMethodName))
        {
            InsertSingleArgHook(songSelectUpdateMethod, importedSongSelectHook);
            patchesApplied++;
        }

        if (ApplyCustomSubtitlePatch(targetModule))
        {
            patchesApplied++;
        }

        if (ApplyVersionLabelPatch(targetModule))
        {
            patchesApplied++;
        }

        if (ApplyControllerDisconnectPausePatch(gameManager))
        {
            patchesApplied++;
        }

        TypeDefinition? basePlayer = targetModule.Types.FirstOrDefault(type => type.Name == "BasePlayer");
        if (basePlayer == null)
        {
            Console.Error.WriteLine("BasePlayer type not found.");
            return 1;
        }

        MethodDefinition? noteMissMethod = basePlayer.Methods.FirstOrDefault(method => HasAnyHookCall(method, NoteMissHookMethodName))
            ?? basePlayer.Methods.FirstOrDefault(IsBasePlayerMissMethod);
        if (noteMissMethod == null)
        {
            Console.Error.WriteLine("BasePlayer miss method not found.");
            return 1;
        }

        MethodReference importedNoteMissHook = targetModule.ImportReference(noteMissHookMethod);
        if (RewriteLegacyHookCalls(noteMissMethod, NoteMissHookMethodName, importedNoteMissHook))
        {
            patchesApplied++;
        }

        if (!HasDesiredHookCall(noteMissMethod, NoteMissHookMethodName))
        {
            InsertTwoArgHook(noteMissMethod, importedNoteMissHook);
            patchesApplied++;
        }

        if (RemoveAssemblyReference(targetModule, LegacyHookAssemblyName))
        {
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
        Instruction first = method.Body.Instructions[0];
        InsertSingleArgHookBefore(method, hook, first);
    }

    private static void InsertMainMenuEnableHook(MethodDefinition method, MethodReference hook)
    {
        Instruction insertBefore = method.Body.Instructions.FirstOrDefault(instruction =>
            instruction.Operand is FieldReference field &&
            string.Equals(field.FieldType.Name, "News", StringComparison.Ordinal))
            ?? method.Body.Instructions[0];

        InsertSingleArgHookBefore(method, hook, insertBefore);
    }

    private static void InsertSingleArgHookBefore(MethodDefinition method, MethodReference hook, Instruction insertBefore)
    {
        ILProcessor il = method.Body.GetILProcessor();
        il.InsertBefore(insertBefore, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(insertBefore, il.Create(OpCodes.Call, hook));
        method.Body.SimplifyMacros();
        method.Body.OptimizeMacros();
    }

    private static void InsertSingleArgReturnIfTrueHook(MethodDefinition method, MethodReference hook)
    {
        ILProcessor il = method.Body.GetILProcessor();
        Instruction first = method.Body.Instructions[0];
        Instruction continueInstruction = il.Create(OpCodes.Nop);
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Call, hook));
        il.InsertBefore(first, il.Create(OpCodes.Brfalse_S, continueInstruction));
        il.InsertBefore(first, il.Create(OpCodes.Ret));
        il.InsertBefore(first, continueInstruction);
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

    private static bool HasDesiredHookCall(MethodDefinition method, string hookMethodName)
    {
        if (method == null || !method.HasBody)
        {
            return false;
        }

        return method.Body.Instructions.Any(instruction => IsDesiredHookCall(instruction, hookMethodName));
    }

    private static bool HasAnyHookCall(MethodDefinition method, string hookMethodName)
    {
        if (method == null || !method.HasBody)
        {
            return false;
        }

        return method.Body.Instructions.Any(instruction =>
            instruction.OpCode == OpCodes.Call &&
            instruction.Operand is MethodReference calledMethod &&
            IsHookMethod(calledMethod, hookMethodName));
    }

    private static bool IsDesiredHookCall(Instruction instruction, string hookMethodName)
    {
        if (instruction.OpCode != OpCodes.Call || instruction.Operand is not MethodReference calledMethod)
        {
            return false;
        }

        return IsHookMethod(calledMethod, hookMethodName) &&
            string.Equals(GetScopeAssemblyName(calledMethod.DeclaringType.Scope), HookAssemblyName, StringComparison.Ordinal);
    }

    private static bool RewriteLegacyHookCalls(MethodDefinition method, string hookMethodName, MethodReference desiredHook)
    {
        if (method == null || !method.HasBody)
        {
            return false;
        }

        bool changed = false;
        foreach (Instruction instruction in method.Body.Instructions)
        {
            if (instruction.OpCode != OpCodes.Call || instruction.Operand is not MethodReference calledMethod)
            {
                continue;
            }

            if (!IsHookMethod(calledMethod, hookMethodName))
            {
                continue;
            }

            string scopeAssemblyName = GetScopeAssemblyName(calledMethod.DeclaringType.Scope);
            if (string.Equals(scopeAssemblyName, HookAssemblyName, StringComparison.Ordinal))
            {
                continue;
            }

            instruction.Operand = desiredHook;
            changed = true;
        }

        if (changed)
        {
            method.Body.SimplifyMacros();
            method.Body.OptimizeMacros();
        }

        return changed;
    }

    private static bool IsHookMethod(MethodReference calledMethod, string hookMethodName)
    {
        return calledMethod.Name == hookMethodName &&
            calledMethod.DeclaringType.FullName == HookTypeName;
    }

    private static string GetScopeAssemblyName(IMetadataScope? scope)
    {
        return scope switch
        {
            AssemblyNameReference assemblyNameReference => assemblyNameReference.Name ?? string.Empty,
            ModuleDefinition module when module.Assembly != null => module.Assembly.Name.Name ?? string.Empty,
            _ => string.Empty
        };
    }

    private static bool RemoveAssemblyReference(ModuleDefinition module, string assemblyName)
    {
        AssemblyNameReference? reference = module.AssemblyReferences.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, assemblyName, StringComparison.Ordinal));
        if (reference == null)
        {
            return false;
        }

        module.AssemblyReferences.Remove(reference);
        return true;
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

    private static bool ApplyControllerDisconnectPausePatch(TypeDefinition gameManager)
    {
        MethodDefinition? disconnectHandler = gameManager.Methods.FirstOrDefault(method =>
            !method.IsConstructor &&
            method.ReturnType.FullName == "System.Void" &&
            method.Parameters.Count == 1 &&
            string.Equals(method.Parameters[0].ParameterType.Name, "ControllerStatusChangedEventArgs", StringComparison.Ordinal));
        if (disconnectHandler == null || !disconnectHandler.HasBody)
        {
            return false;
        }

        if (disconnectHandler.Body.Instructions.Count == 1 &&
            disconnectHandler.Body.Instructions[0].OpCode == OpCodes.Ret)
        {
            return false;
        }

        disconnectHandler.Body.Instructions.Clear();
        disconnectHandler.Body.Variables.Clear();
        disconnectHandler.Body.ExceptionHandlers.Clear();
        disconnectHandler.Body.InitLocals = false;
        disconnectHandler.Body.GetILProcessor().Append(disconnectHandler.Body.GetILProcessor().Create(OpCodes.Ret));
        disconnectHandler.Body.OptimizeMacros();
        return true;
    }

    private static bool ApplyCustomSubtitlePatch(ModuleDefinition module)
    {
        TypeDefinition? globalVariables = module.Types.FirstOrDefault(type => type.Name == "GlobalVariables");
        if (globalVariables == null)
        {
            Console.Error.WriteLine("GlobalVariables type not found.");
            return false;
        }

        MethodDefinition? startMethod = globalVariables.Methods.FirstOrDefault(method => method.Name == "Start" && !method.HasParameters && method.HasBody);
        if (startMethod == null)
        {
            Console.Error.WriteLine("GlobalVariables.Start() not found.");
            return false;
        }

        FieldDefinition[] subtitleFields = SubtitleFieldNameGroups
            .Select(group => group
                .Select(name => globalVariables.Fields.FirstOrDefault(field => field.Name == name))
                .FirstOrDefault(field => field != null))
            .Where(field => field != null)
            .Cast<FieldDefinition>()
            .ToArray();
        if (subtitleFields.Length != SubtitleFieldNameGroups.Length)
        {
            Console.Error.WriteLine("Could not resolve the main menu subtitle fields.");
            return false;
        }

        MethodDefinition helperMethod = EnsureCustomTagHelper(module, globalVariables, subtitleFields);
        bool hadExistingCall = HasDirectHelperCall(startMethod, helperMethod);
        if (!hadExistingCall)
        {
            InsertHelperCall(startMethod, module.ImportReference(helperMethod));
        }

        return !hadExistingCall || helperMethod.Body.Instructions.Count > 0;
    }

    private static MethodDefinition EnsureCustomTagHelper(ModuleDefinition module, TypeDefinition globalVariables, FieldDefinition[] subtitleFields)
    {
        MethodDefinition? helper = globalVariables.Methods.FirstOrDefault(method => method.Name == CustomTagHelperMethodName);
        if (helper == null)
        {
            helper = new MethodDefinition(
                CustomTagHelperMethodName,
                MethodAttributes.Private | MethodAttributes.HideBySig,
                module.TypeSystem.Void);
            globalVariables.Methods.Add(helper);
        }

        helper.Body = new MethodBody(helper)
        {
            InitLocals = true
        };
        helper.Body.Variables.Add(new VariableDefinition(module.ImportReference(typeof(string[]))));

        ILProcessor il = helper.Body.GetILProcessor();
        EmitArrayConstruction(il, module, CustomTags);
        il.Append(il.Create(OpCodes.Stloc_0));

        foreach (FieldDefinition field in subtitleFields)
        {
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ldloc_0));
            il.Append(il.Create(OpCodes.Stfld, module.ImportReference(field)));
        }

        il.Append(il.Create(OpCodes.Ret));
        helper.Body.SimplifyMacros();
        helper.Body.OptimizeMacros();
        return helper;
    }

    private static void EmitArrayConstruction(ILProcessor il, ModuleDefinition module, IReadOnlyList<string> values)
    {
        il.Append(il.Create(OpCodes.Ldc_I4, values.Count));
        il.Append(il.Create(OpCodes.Newarr, module.TypeSystem.String));
        for (int index = 0; index < values.Count; index++)
        {
            il.Append(il.Create(OpCodes.Dup));
            il.Append(il.Create(OpCodes.Ldc_I4, index));
            il.Append(il.Create(OpCodes.Ldstr, values[index]));
            il.Append(il.Create(OpCodes.Stelem_Ref));
        }
    }

    private static bool HasDirectHelperCall(MethodDefinition method, MethodDefinition helperMethod)
    {
        if (!method.HasBody)
        {
            return false;
        }

        return method.Body.Instructions.Any(instruction =>
            instruction.OpCode == OpCodes.Call &&
            instruction.Operand is MethodReference called &&
            called.Name == helperMethod.Name &&
            called.DeclaringType.FullName == helperMethod.DeclaringType.FullName);
    }

    private static void InsertHelperCall(MethodDefinition targetMethod, MethodReference helperMethod)
    {
        ILProcessor il = targetMethod.Body.GetILProcessor();
        Instruction first = targetMethod.Body.Instructions[0];
        il.InsertBefore(first, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(first, il.Create(OpCodes.Call, helperMethod));
        targetMethod.Body.SimplifyMacros();
        targetMethod.Body.OptimizeMacros();
    }

    private static bool ApplyVersionLabelPatch(ModuleDefinition module)
    {
        TypeDefinition? mainMenu = module.Types.FirstOrDefault(type => type.Name == "MainMenu");
        if (mainMenu == null)
        {
            Console.Error.WriteLine("MainMenu type not found for version label patch.");
            return false;
        }

        MethodDefinition? onEnable = mainMenu.Methods.FirstOrDefault(method => method.Name == "OnEnable" && !method.HasParameters && method.HasBody);
        if (onEnable == null)
        {
            Console.Error.WriteLine("MainMenu.OnEnable() not found.");
            return false;
        }

        FieldDefinition? versionLabelField = mainMenu.Fields.FirstOrDefault(field => field.FieldType.FullName == "TMPro.TextMeshProUGUI");
        if (versionLabelField == null)
        {
            Console.Error.WriteLine("MainMenu version label field not found.");
            return false;
        }

        if (HasReplacementAssignment(onEnable, versionLabelField))
        {
            return false;
        }

        InsertReplacementAssignment(module, onEnable, versionLabelField);
        return true;
    }

    private static bool HasReplacementAssignment(MethodDefinition onEnable, FieldDefinition versionLabelField)
    {
        var instructions = onEnable.Body.Instructions;
        for (int index = 0; index <= instructions.Count - 4; index++)
        {
            if (instructions[index].OpCode == OpCodes.Ldarg_0 &&
                instructions[index + 1].OpCode == OpCodes.Ldfld &&
                instructions[index + 1].Operand is FieldReference field &&
                field.Name == versionLabelField.Name &&
                instructions[index + 2].OpCode == OpCodes.Ldstr &&
                Equals(instructions[index + 2].Operand, VersionReplacementText))
            {
                return true;
            }
        }

        return false;
    }

    private static void InsertReplacementAssignment(ModuleDefinition module, MethodDefinition onEnable, FieldDefinition versionLabelField)
    {
        MethodReference textSetter = FindTextSetter(module, versionLabelField);
        ILProcessor il = onEnable.Body.GetILProcessor();
        Instruction insertBefore = FindBaseOnEnableCall(onEnable) ?? onEnable.Body.Instructions[0];

        il.InsertBefore(insertBefore, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(insertBefore, il.Create(OpCodes.Ldfld, module.ImportReference(versionLabelField)));
        il.InsertBefore(insertBefore, il.Create(OpCodes.Ldstr, VersionReplacementText));
        il.InsertBefore(insertBefore, il.Create(OpCodes.Callvirt, textSetter));
        onEnable.Body.SimplifyMacros();
        onEnable.Body.OptimizeMacros();
    }

    private static MethodReference FindTextSetter(ModuleDefinition module, FieldDefinition versionLabelField)
    {
        TypeReference? currentType = versionLabelField.FieldType;
        while (currentType != null)
        {
            TypeDefinition? resolved = currentType.Resolve();
            if (resolved == null)
            {
                break;
            }

            MethodDefinition? setter = resolved.Methods.FirstOrDefault(method => method.Name == "set_text" && method.Parameters.Count == 1);
            if (setter != null)
            {
                return module.ImportReference(setter);
            }

            currentType = resolved.BaseType;
        }

        throw new InvalidOperationException("TMPro text setter not found.");
    }

    private static Instruction? FindBaseOnEnableCall(MethodDefinition onEnable)
    {
        return onEnable.Body.Instructions.FirstOrDefault(instruction =>
            instruction.OpCode == OpCodes.Call &&
            instruction.Operand is MethodReference called &&
            called.Name == "OnEnable");
    }
}
