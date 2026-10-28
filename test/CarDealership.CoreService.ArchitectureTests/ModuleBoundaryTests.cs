using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace CarDealership.CoreService.Architecture;

/// <summary>
/// Ngày 16 (ADR-003, Req 28.1, 28.8): kiểm ranh giới 7 module nghiệp vụ.
/// Ranh giới không do compiler bảo đảm (dùng chung project Domain), nên khóa bằng NetArchTest:
///  - Mỗi module không được phụ thuộc trực tiếp vào namespace nội bộ của module khác;
///    liên kết chỉ qua *.Application.Contracts.&lt;Module&gt; (thêm ở các phase sau) hoặc Domain.Shared.
///  - Mỗi module phải có đúng một lớp &lt;Module&gt;DomainModule.
/// </summary>
public class ModuleBoundaryTests
{
    private const string Root = "CarDealership.CoreService";

    // 7 module nghiệp vụ (ADR-003). Dùng namespace gốc của module trong project Domain.
    private static readonly string[] Modules =
    {
        "Catalog", "Showrooms", "Leads", "Identity", "Settings", "Audit", "Analytics"
    };

    private static readonly Assembly DomainAssembly = typeof(CarDealership.CoreService.Catalog.CatalogDomainModule).Assembly;

    [Fact]
    public void Each_module_has_exactly_one_AbpModule_class()
    {
        foreach (var module in Modules)
        {
            var ns = $"{Root}.{module}";
            var moduleClasses = Types.InAssembly(DomainAssembly)
                .That().ResideInNamespace(ns)
                .And().HaveNameEndingWith("DomainModule")
                .GetTypes()
                .ToList();

            moduleClasses.Count.ShouldBe(1,
                $"Module '{module}' phai co dung 1 lop <Module>DomainModule trong namespace {ns}.");
        }
    }

    [Theory]
    [MemberData(nameof(ModulePairs))]
    public void Module_does_not_depend_on_another_module_internal_namespace(string module, string other)
    {
        var moduleNs = $"{Root}.{module}";
        var otherNs = $"{Root}.{other}";

        var result = Types.InAssembly(DomainAssembly)
            .That().ResideInNamespace(moduleNs)
            .ShouldNot().HaveDependencyOn(otherNs)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"Module '{module}' KHONG duoc phu thuoc truc tiep vao namespace noi bo cua '{other}'. "
            + "Lien ket chi qua Application.Contracts. Loai vi pham: "
            + string.Join(", ", result.FailingTypeNames ?? new List<string>()));
    }

    public static IEnumerable<object[]> ModulePairs()
    {
        foreach (var a in Modules)
        {
            foreach (var b in Modules)
            {
                if (a != b)
                {
                    yield return new object[] { a, b };
                }
            }
        }
    }
}
