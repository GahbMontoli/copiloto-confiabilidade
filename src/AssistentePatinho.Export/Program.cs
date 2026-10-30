using AssistentePatinho.Core;

if (args.Length is < 1 or > 2)
{
    Console.Error.WriteLine("Uso: AssistentePatinho.Export <sample-cmms-data.json> [diretório-saída]");
    return 2;
}

try
{
    var sourcePath = Path.GetFullPath(args[0]);
    var outputDirectory = args.Length == 2
        ? Path.GetFullPath(args[1])
        : Path.Combine(Environment.CurrentDirectory, "artifacts", "power-bi");
    var sourceBytes = await File.ReadAllBytesAsync(sourcePath);
    var dataset = await new SimulatedCmmsConnector(sourcePath).LoadAsync();
    var result = await CmmsBiExporter.ExportAsync(
        outputDirectory,
        dataset,
        new CmmsBiExportOptions(
            RunId: "cmms-sample-v1",
            SourceId: Path.GetFileName(sourcePath),
            SourceSha256: CmmsBiExporter.ComputeSha256(sourceBytes),
            ExportedAtUtc: DateTimeOffset.UtcNow));

    Console.WriteLine($"Pacote Power BI: {result.OutputDirectory}");
    Console.WriteLine(
        $"Ativos: {result.AssetCount}; ordens: {result.WorkOrderCount}; " +
        $"verificações: {result.CheckCount}; peças: {result.PartCount}");
    return 0;
}
catch (Exception exception) when (
    exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
{
    Console.Error.WriteLine($"Falha ao exportar dados para Power BI: {exception.Message}");
    return 1;
}
