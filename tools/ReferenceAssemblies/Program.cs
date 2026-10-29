using Mono.Cecil;
using Mono.Cecil.Cil;
using System.IO.Compression;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: ReferenceAssemblies <wrapper-directory> <output.zip>");
    return 2;
}

var input = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
using var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(input);
resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(object).Assembly.Location)!);
using var outputFile = File.Create(output);
using var archive = new ZipArchive(outputFile, ZipArchiveMode.Create);
var count = 0;
foreach (var path in Directory.EnumerateFiles(input, "*.dll").Order(StringComparer.Ordinal))
{
    using var assembly = AssemblyDefinition.ReadAssembly(path, new ReaderParameters { AssemblyResolver = resolver });
    foreach (var type in assembly.MainModule.GetTypes())
    {
        foreach (var method in type.Methods)
        {
            if (method.HasBody)
            {
                method.Body = new MethodBody(method);
                method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldnull));
                method.Body.Instructions.Add(Instruction.Create(OpCodes.Throw));
            }

            for (var i = method.CustomAttributes.Count - 1; i >= 0; i--)
                if (method.CustomAttributes[i].AttributeType.Name is "CachedScanResultsAttribute" or "CallerCountAttribute")
                    method.CustomAttributes.RemoveAt(i);
        }
    }

    assembly.MainModule.Resources.Clear();
    var marker = typeof(System.Runtime.CompilerServices.ReferenceAssemblyAttribute).GetConstructor(Type.EmptyTypes)!;
    assembly.CustomAttributes.Add(new CustomAttribute(assembly.MainModule.ImportReference(marker)));
    using var memory = new MemoryStream();
    assembly.Write(memory);
    var bytes = memory.ToArray();
    var entry = archive.CreateEntry(Path.GetFileName(path), CompressionLevel.Optimal);
    entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    using (var stream = entry.Open())
        stream.Write(bytes);
    count++;
}

Console.WriteLine("Reference assemblies: " + count);
return 0;
