You are not using the latest version of the tool, please update.
Latest version is '10.0.1.8346' (yours is '9.1.0.7988')
Error decompiling @02000017 GarrisonDoSomething.MySettings
in assembly "D:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\GarrisonDoSomething\bin\Win64_Shipping_Client\GarrisonDoSomething.dll"
 ---> System.BadImageFormatException: Invalid method header: 0xC7 0x73
   at System.Reflection.Metadata.MethodBodyBlock.Create(BlobReader reader) in offset 130
   at System.Reflection.Metadata.PEReaderExtensions.GetMethodBody(PEReader peReader, Int32 relativeVirtualAddress) in offset 52
   at ICSharpCode.Decompiler.Metadata.PEFile.GetMethodBody(Int32 rva) in PEFile.cs:line 70
   at ICSharpCode.Decompiler.CSharp.RecordDecompiler.DecompileBody(IMethod method) in RecordDecompiler.cs:line 1151
   at ICSharpCode.Decompiler.CSharp.RecordDecompiler.<DetectAutomaticProperties>g__IsAutoGetter|15_1(IMethod method, IField& field) in RecordDecompiler.cs:line 114
   at ICSharpCode.Decompiler.CSharp.RecordDecompiler.<DetectAutomaticProperties>g__IsAutoProperty|15_0(IProperty p, IField& field) in RecordDecompiler.cs:line 87
   at ICSharpCode.Decompiler.CSharp.RecordDecompiler.DetectAutomaticProperties() in RecordDecompiler.cs:line 71
   at ICSharpCode.Decompiler.CSharp.RecordDecompiler..ctor(IDecompilerTypeSystem dts, ITypeDefinition recordTypeDef, DecompilerSettings settings, CancellationToken cancellationToken) in RecordDecompiler.cs:line 59
   at ICSharpCode.Decompiler.CSharp.CSharpDecompiler.DoDecompile(ITypeDefinition typeDef, DecompileRun decompileRun, ITypeResolveContext decompilationContext) in CSharpDecompiler.cs:line 1320
-- continuing with outer exception (ICSharpCode.Decompiler.DecompilerException) --
   at ICSharpCode.Decompiler.CSharp.CSharpDecompiler.DoDecompile(ITypeDefinition typeDef, DecompileRun decompileRun, ITypeResolveContext decompilationContext) in CSharpDecompiler.cs:line 1478
   at ICSharpCode.Decompiler.CSharp.CSharpDecompiler.DoDecompileTypes(IEnumerable`1 types, DecompileRun decompileRun, ITypeResolveContext decompilationContext, SyntaxTree syntaxTree) in CSharpDecompiler.cs:line 659
   at ICSharpCode.Decompiler.CSharp.CSharpDecompiler.DecompileType(FullTypeName fullTypeName) in CSharpDecompiler.cs:line 992
   at ICSharpCode.Decompiler.CSharp.CSharpDecompiler.DecompileTypeAsString(FullTypeName fullTypeName) in CSharpDecompiler.cs:line 1005
   at ICSharpCode.ILSpyCmd.ILSpyCmdProgram.Decompile(String assemblyFileName, TextWriter output, String typeName) in IlspyCmdProgram.cs:line 399
   at ICSharpCode.ILSpyCmd.ILSpyCmdProgram.<OnExecuteAsync>g__PerformPerFileAction|83_0(String fileName, <>c__DisplayClass83_0&) in IlspyCmdProgram.cs:line 311
   at ICSharpCode.ILSpyCmd.ILSpyCmdProgram.<OnExecuteAsync>d__83.MoveNext() in IlspyCmdProgram.cs:line 232
