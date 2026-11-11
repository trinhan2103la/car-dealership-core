using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace CarDealership.CoreService.Catalog.Seed;

/// <summary>Cấu hình seed Demo_Data catalog. Chỉ <c>dev</c> và <c>demo</c> tạo dữ liệu; <c>prod</c> hoặc thiếu biến thì bỏ qua.</summary>
public class CatalogDemoSeedOptions
{
    public const string EnvironmentVariable = "SEED_ENVIRONMENT";
    public const string DataPathVariable = "SEED_DEMO_PATH";

    public static readonly string[] SeedingEnvironments = { "dev", "demo" };
    public static readonly string[] KnownEnvironments = { "dev", "demo", "prod" };

    public string? Environment { get; set; }

    /// <summary>Thư mục chứa <c>catalog.json</c> và <c>images/</c>. Mặc định: <c>seed/demo</c> cạnh file chạy (DbMigrator copy vào output).</summary>
    public string DataPath { get; set; } = Path.Combine(AppContext.BaseDirectory, "seed", "demo");
}

// ---------------------------------------------------------------- catalog.json

public sealed class SeedCatalog
{
    public List<SeedBrand> Brands { get; set; } = new();
}

public sealed class SeedBrand
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public List<SeedCarModel> CarModels { get; set; } = new();
}

public sealed class SeedCarModel
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsFeatured { get; set; }
    public int DisplayOrder { get; set; }
    public List<SeedVariant> Variants { get; set; } = new();
    public List<SeedImage> Images { get; set; } = new();
}

public sealed class SeedVariant
{
    public string Name { get; set; } = string.Empty;
    public string BodyType { get; set; } = string.Empty;
    public string Fuel { get; set; } = string.Empty;
    public string Transmission { get; set; } = string.Empty;
    public int Seats { get; set; }
    public string Engine { get; set; } = string.Empty;
    public string Power { get; set; } = string.Empty;
    public decimal? ReferencePrice { get; set; }
    public bool ContactForPrice { get; set; }
}

public sealed class SeedImage
{
    /// <summary>Tên gốc, không đuôi: ba file <c>{file}-sm.webp</c>, <c>-md</c>, <c>-lg</c> trong <c>images/</c>.</summary>
    public string File { get; set; } = string.Empty;
    public string Alt { get; set; } = string.Empty;
    public bool Cover { get; set; }
}

// ---------------------------------------------------------------- images/manifest.json

public sealed class SeedImageSource
{
    public string File { get; set; } = string.Empty;
    public string? Source { get; set; }
    public string? License { get; set; }
    public string? Author { get; set; }
}

/// <summary>Đọc và kiểm toàn bộ dữ liệu seed TRƯỚC khi ghi bất cứ thứ gì. Lỗi nêu tên file, không nêu nội dung.</summary>
public static class CatalogDemoSeedLoader
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string ImagesPath(string dataPath) => Path.Combine(dataPath, "images");

    public static string VariantPath(string dataPath, string file, string size) =>
        Path.Combine(ImagesPath(dataPath), $"{file}-{size}.webp");

    public static SeedCatalog Load(string dataPath, IReadOnlyList<string> sizes)
    {
        var catalogFile = Path.Combine(dataPath, "catalog.json");
        var manifestFile = Path.Combine(ImagesPath(dataPath), "manifest.json");
        var catalog = Read<SeedCatalog>(catalogFile);
        var manifest = Read<List<SeedImageSource>>(manifestFile)
            .GroupBy(m => m.File, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var problems = new List<string>();
        foreach (var image in catalog.Brands.SelectMany(b => b.CarModels).SelectMany(m => m.Images))
        {
            if (!manifest.TryGetValue(image.File, out var entry))
            {
                problems.Add($"{image.File}: thiếu mục trong manifest.json");
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.Source))
            {
                problems.Add($"{image.File}: manifest thiếu 'source'");
            }

            if (string.IsNullOrWhiteSpace(entry.License))
            {
                problems.Add($"{image.File}: manifest thiếu 'license'");
            }

            foreach (var size in sizes)
            {
                var path = VariantPath(dataPath, image.File, size);
                if (!File.Exists(path))
                {
                    problems.Add($"{image.File}: thiếu file {Path.GetFileName(path)}");
                }
                else if (!IsWebP(path))
                {
                    problems.Add($"{image.File}: {Path.GetFileName(path)} không phải WebP");
                }
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException("Dữ liệu seed demo không hợp lệ: " + string.Join("; ", problems));
        }

        return catalog;
    }

    private static T Read<T>(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Dữ liệu seed demo không hợp lệ: không thấy {Path.GetFileName(path)}");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json)
                   ?? throw new InvalidOperationException($"Dữ liệu seed demo không hợp lệ: {Path.GetFileName(path)} rỗng");
        }
        catch (JsonException)
        {
            throw new InvalidOperationException($"Dữ liệu seed demo không hợp lệ: {Path.GetFileName(path)} không phải JSON hợp lệ");
        }
    }

    private static bool IsWebP(string path)
    {
        Span<byte> head = stackalloc byte[12];
        using var stream = File.OpenRead(path);
        return stream.Read(head) == 12
               && head[..4].SequenceEqual("RIFF"u8)
               && head[8..12].SequenceEqual("WEBP"u8);
    }
}
