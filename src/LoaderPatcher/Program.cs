using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length == 3 && args[0] == "--offline-assembly")
{
    using var assembly = AssemblyDefinition.ReadAssembly(args[1]);
    var launch = assembly.MainModule.GetType("MelonLoader.MelonLaunchOptions");
    var generator = launch.NestedTypes.Single(t => t.Name == "Il2CppAssemblyGenerator");
    var getter = generator.Methods.Single(m => m.Name == "get_OfflineMode");
    getter.Body.Instructions.Clear();
    getter.Body.ExceptionHandlers.Clear();
    getter.Body.Variables.Clear();
    getter.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1));
    getter.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
    assembly.Write(args[2]);
    return 0;
}

if (args.Length == 4 && args[0] == "--fix-android-first-run")
{
    using var loader = AssemblyDefinition.ReadAssembly(args[1]);
    using var helper = AssemblyDefinition.ReadAssembly(args[2]);
    var module = loader.MainModule;
    var core = module.GetType("MelonLoader.Core");
    var initialize = core.Methods.Single(m => m.Name == "Initialize");
    var start = core.Methods.Single(m => m.Name == "Start");
    var source = helper.MainModule.GetType("Minkyu.LoaderFixes.BootstrapEntrypoint").Methods.Single(m => m.Name == "Install");
    if (module.GetType("Minkyu.LoaderFixes.BootstrapEntrypoint") != null)
        throw new InvalidDataException("First-run fix already present");
    var host = new TypeDefinition("Minkyu.LoaderFixes", "BootstrapEntrypoint", Mono.Cecil.TypeAttributes.NotPublic | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed, module.TypeSystem.Object);
    module.Types.Add(host);
    var install = new MethodDefinition("Install", source.Attributes, module.TypeSystem.Void);
    install.Parameters.Add(new ParameterDefinition("payload", Mono.Cecil.ParameterAttributes.None, module.TypeSystem.String));
    host.Methods.Add(install);
    foreach (var instruction in source.Body.Instructions)
    {
        var copy = Instruction.Create(OpCodes.Nop);
        copy.OpCode = instruction.OpCode;
        copy.Operand = instruction.Operand switch
        {
            MethodReference method => module.ImportReference(method),
            TypeReference type => module.ImportReference(type),
            _ => instruction.Operand
        };
        install.Body.Instructions.Add(copy);
    }

    var initialized = initialize.Body.Instructions.Single(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "MelonLoader.Utils.APKAssetManager" && m.Name == "Initialize");
    var il = initialize.Body.GetILProcessor();
    var payload = Instruction.Create(OpCodes.Ldstr, Convert.ToBase64String(File.ReadAllBytes(args[2])));
    il.InsertAfter(initialized, payload);
    il.InsertAfter(payload, Instruction.Create(OpCodes.Call, install));
    // Install interop after the bundled wrappers and dependencies are copied.
    var deferred = new List<MethodReference>();
    foreach (var instruction in initialize.Body.Instructions)
        if (instruction.Operand is MethodReference m && m.Name == "Install" &&
            m.DeclaringType.FullName is "MelonLoader.Fixes.Il2CppInteropFixes" or "MelonLoader.Fixes.Il2CppICallInjector")
        {
            deferred.Add(m);
            instruction.OpCode = OpCodes.Nop;
            instruction.Operand = null;
        }

    if (deferred.Count != 2)
        throw new InvalidDataException("Expected exactly two early interop initialization calls");
    var beforeMods = start.Body.Instructions.First(i =>
        i.Operand is FieldReference f && f.Name == "OnPreModsLoaded" ||
        i.Operand is MethodReference m && m.Name == "get_OnPreModsLoaded");
    var calls = deferred.Select(m => Instruction.Create(OpCodes.Call, m)).ToList();
    foreach (var call in calls)
        start.Body.GetILProcessor().InsertBefore(beforeMods, call);
    foreach (var i in start.Body.Instructions)
        if (i.Operand == beforeMods)
            i.Operand = calls[0];
    foreach (var method in new[] { initialize, start })
        foreach (var i in method.Body.Instructions)
            i.OpCode = ExpandShortBranch(i.OpCode);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[3]))!);
    loader.Write(args[3]);
    Console.WriteLine("ANDROID_FIRST_RUN_FIXED copy-bundled-dependencies=true defer-interop-until-generated=true");
    return 0;
}

if (args.Length == 4 && args[0] == "--embed-scripting-guard")
{
    using var support = AssemblyDefinition.ReadAssembly(args[1]);
    using var helper = AssemblyDefinition.ReadAssembly(args[2]);
    var module = support.MainModule;
    var initialize = module.GetType("MelonLoader.Support.Main").Methods.Single(method => method.Name == "Initialize");
    var il = initialize.Body.GetILProcessor();
    var scene = initialize.Body.Instructions.Single(instruction => instruction.Operand is MethodReference method && method.DeclaringType.FullName == "MelonLoader.Support.SceneHandler" && method.Name == "Init");
    var source = helper.MainModule.GetType("CustomAlbums.LoaderFixes.EmbeddedBootstrap").Methods.Single(method => method.Name == "Install");
    var container = new TypeDefinition("CustomAlbums.LoaderFixes", "EmbeddedBootstrap", Mono.Cecil.TypeAttributes.NotPublic | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed, module.TypeSystem.Object);
    module.Types.Add(container);
    var bootstrap = new MethodDefinition("Install", source.Attributes, module.TypeSystem.Void);
    bootstrap.Parameters.Add(new ParameterDefinition("payload", Mono.Cecil.ParameterAttributes.None, module.TypeSystem.String));
    container.Methods.Add(bootstrap);
    foreach (var instruction in source.Body.Instructions)
    {
        var copied = Instruction.Create(OpCodes.Nop);
        copied.OpCode = instruction.OpCode;
        copied.Operand = instruction.Operand switch
        {
            MethodReference method => module.ImportReference(method),
            TypeReference type => module.ImportReference(type),
            _ => instruction.Operand
        };
        bootstrap.Body.Instructions.Add(copied);
    }

    var added = new[]
    {
        Instruction.Create(OpCodes.Ldstr, Convert.ToBase64String(File.ReadAllBytes(args[2]))),
        Instruction.Create(OpCodes.Call, bootstrap),
    };
    foreach (var instruction in added)
        il.InsertBefore(scene, instruction);
    foreach (var instruction in initialize.Body.Instructions)
        if (instruction.Operand == scene)
            instruction.Operand = added[0];
    foreach (var instruction in initialize.Body.Instructions)
        instruction.OpCode = ExpandShortBranch(instruction.OpCode);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[3]))!);
    support.Write(args[3]);
    Console.WriteLine("SCRIPTING_GUARD_EMBEDDED " + args[3]);
    return 0;
}

if (args.Length == 3 && args[0] == "--fix-arm64-field-default")
{
    using var runtime = AssemblyDefinition.ReadAssembly(args[1]);
    var finder = runtime.MainModule.GetType("Il2CppInterop.Runtime.Injection.Hooks.Class_GetFieldDefaultValue_Hook").Methods.Single(method => method.Name == "FindClassGetFieldDefaultValueXref");
    var marker = finder.Body.Instructions.Single(instruction => instruction.Operand is string text && text.StartsWith("Field::StaticGetValueInternal:"));
    // ARM64 xrefs already point to the target. The fallback scanner expects x86.
    var scan = marker;
    while (scan is not null && !(scan.OpCode.Code == Code.Call && scan.Operand is MethodReference method && method.Name == "LogTrace"))
        scan = scan.Next;
    var candidateLoad = scan?.Next ?? throw new InvalidDataException("Missing candidate load");
    if (candidateLoad.OpCode.Code != Code.Ldloc_S || candidateLoad.Operand is not VariableDefinition)
        throw new InvalidDataException("Unexpected candidate load IL");
    finder.Body.GetILProcessor().InsertAfter(candidateLoad, Instruction.Create(OpCodes.Ret));
    foreach (var instruction in finder.Body.Instructions)
        instruction.OpCode = ExpandShortBranch(instruction.OpCode);
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
    runtime.Write(args[2]);
    Console.WriteLine("ARM64_FIELD_DEFAULT_RESOLVER_FIXED " + args[2]);
    return 0;
}

if (args.Length == 4 && args[0] == "--fix-arm64-generic")
{
    using var runtime = AssemblyDefinition.ReadAssembly(args[1]);
    using var helpers = AssemblyDefinition.ReadAssembly(args[2]);
    var module = runtime.MainModule;
    var source = helpers.MainModule.GetType("CustomAlbums.LoaderFixes.Arm64GenericResolver").Methods.Single(method => method.Name == "Resolve2019");
    var host = new TypeDefinition("CustomAlbums.LoaderFixes", "Arm64GenericResolver", Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Abstract | Mono.Cecil.TypeAttributes.Sealed, module.TypeSystem.Object);
    module.Types.Add(host);
    var target = new MethodDefinition(source.Name, source.Attributes, module.ImportReference(source.ReturnType));
    host.Methods.Add(target);
    foreach (var parameter in source.Parameters)
        target.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, module.ImportReference(parameter.ParameterType)));
    foreach (var variable in source.Body.Variables)
        target.Body.Variables.Add(new VariableDefinition(module.ImportReference(variable.VariableType)));
    target.Body.InitLocals = source.Body.InitLocals;
    target.Body.MaxStackSize = source.Body.MaxStackSize;
    var copied = new Dictionary<Instruction, Instruction>();
    foreach (var instruction in source.Body.Instructions)
    {
        var next = Instruction.Create(OpCodes.Nop);
        next.OpCode = instruction.OpCode;
        next.Operand = instruction.Operand switch
        {
            MethodReference method => module.ImportReference(method),
            TypeReference type => module.ImportReference(type),
            FieldReference field => module.ImportReference(field),
            ParameterDefinition parameter => target.Parameters[parameter.Index],
            VariableDefinition variable => target.Body.Variables[variable.Index],
            _ => instruction.Operand
        };
        copied[instruction] = next;
        target.Body.Instructions.Add(next);
    }

    foreach (var instruction in target.Body.Instructions)
    {
        if (instruction.Operand is Instruction branch)
            instruction.Operand = copied[branch];
        else if (instruction.Operand is Instruction[] branches)
            instruction.Operand = branches.Select(branch => copied[branch]).ToArray();
    }

    var resolver = module.GetType("Il2CppInterop.Runtime.Injection.Hooks.GenericMethod_GetMethod_Hook").Methods.Single(method => method.Name == "FindTargetMethod");
    var export = module.GetType("Il2CppInterop.Runtime.Injection.InjectorHelpers").Methods.Single(method => method.Name == "GetIl2CppExport");
    resolver.Body.Instructions.Clear();
    resolver.Body.Variables.Clear();
    resolver.Body.ExceptionHandlers.Clear();
    resolver.Body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, "il2cpp_object_get_virtual_method"));
    resolver.Body.Instructions.Add(Instruction.Create(OpCodes.Call, export));
    resolver.Body.Instructions.Add(Instruction.Create(OpCodes.Call, target));
    resolver.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[3]))!);
    runtime.Write(args[3]);
    Console.WriteLine("ARM64_GENERIC_RESOLVER_FIXED " + args[3]);
    return 0;
}

if (args.Length == 3 && args[0] == "--bridge-init-setters")
{
    using var assembly = AssemblyDefinition.ReadAssembly(args[1]);
    var count = 0;
    foreach (var type in assembly.MainModule.GetTypes())
    {
        foreach (var setter in type.Methods.Where(method => method.Name.StartsWith("set_", StringComparison.Ordinal) && method.ReturnType is RequiredModifierType modifier && modifier.ModifierType.FullName == "System.Runtime.CompilerServices.IsExternalInit").ToList())
        {
            var bridge = new MethodDefinition(setter.Name, setter.Attributes, assembly.MainModule.TypeSystem.Void);
            foreach (var parameter in setter.Parameters)
                bridge.Parameters.Add(new ParameterDefinition(parameter.Name, parameter.Attributes, parameter.ParameterType));
            var il = bridge.Body.GetILProcessor();
            if (!setter.IsStatic)
                il.Emit(OpCodes.Ldarg_0);
            foreach (var parameter in bridge.Parameters)
                il.Emit(OpCodes.Ldarg, parameter);
            il.Emit(OpCodes.Call, setter);
            il.Emit(OpCodes.Ret);
            type.Methods.Add(bridge);
            count++;
        }
    }

    if (count == 0)
        throw new InvalidDataException("No init setters found; refusing unchanged output.");
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
    assembly.Write(args[2]);
    Console.WriteLine($"BRIDGED {count} init setters to {args[2]}");
    return 0;
}

Console.Error.WriteLine("Usage: LoaderPatcher <mode> <input> [helper] <output>");
Console.Error.WriteLine("Modes: --offline-assembly, --fix-android-first-run, --embed-scripting-guard, --fix-arm64-field-default, --fix-arm64-generic, --bridge-init-setters");
return 2;
static OpCode ExpandShortBranch(OpCode opcode) => opcode.Code switch
{
    Code.Br_S => OpCodes.Br,
    Code.Brfalse_S => OpCodes.Brfalse,
    Code.Brtrue_S => OpCodes.Brtrue,
    Code.Beq_S => OpCodes.Beq,
    Code.Bge_S => OpCodes.Bge,
    Code.Bge_Un_S => OpCodes.Bge_Un,
    Code.Bgt_S => OpCodes.Bgt,
    Code.Bgt_Un_S => OpCodes.Bgt_Un,
    Code.Ble_S => OpCodes.Ble,
    Code.Ble_Un_S => OpCodes.Ble_Un,
    Code.Blt_S => OpCodes.Blt,
    Code.Blt_Un_S => OpCodes.Blt_Un,
    Code.Bne_Un_S => OpCodes.Bne_Un,
    Code.Leave_S => OpCodes.Leave,
    _ => opcode
};
