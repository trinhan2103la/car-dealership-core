using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using CarDealership.CoreService.Audit;
using CarDealership.CoreService.Catalog.Brands;
using CarDealership.CoreService.Identity.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Validation;
using Xunit;

namespace CarDealership.CoreService.Catalog;

/// <summary>
/// Brand trên PostgreSQL thật, UoW có transaction: 400 theo trường, 409 trùng (kể cả đồng thời), 404, ConcurrencyStamp,
/// đúng một Audit_Log mỗi thay đổi, rollback khi audit lỗi. (403 kiểm ở HttpApi.Host.Tests với pipeline thật.)
/// </summary>
[Collection(CoreServiceTestConsts.CollectionDefinitionName)]
[Trait("Category", "Integration")]
public class BrandAppServiceTests : CatalogTestBase
{
    [Fact]
    public async Task Create_stores_brand_as_active_with_normalized_slug_and_trimmed_name()
    {
        var created = await AsAdminAsync(() => Brands.CreateAsync(new SaveBrandInput
        {
            Name = "  Toyota Vietnam  ",
            Slug = "  TOYOTA ",
            DisplayOrder = 3,
        }));

        created.Name.ShouldBe("Toyota Vietnam");
        created.Slug.ShouldBe("toyota");
        created.Status.ShouldBe(BrandStatus.Active);
        created.DisplayOrder.ShouldBe(3);
        created.ConcurrencyStamp.ShouldNotBeNullOrEmpty();
        created.CreationTime.Kind.ShouldBe(DateTimeKind.Utc);

        var loaded = await AsAdminAsync(() => Brands.GetAsync(created.Id));
        loaded.Slug.ShouldBe("toyota");
    }

    [Fact]
    public async Task Invalid_input_returns_every_field_error_in_one_response()
    {
        var ex = await Should.ThrowAsync<AbpValidationException>(() => AsAdminAsync(() => Brands.CreateAsync(new SaveBrandInput
        {
            Name = "   ",
            Slug = "-bad-",
            DisplayOrder = 10_000,
        })));

        var members = ex.ValidationErrors.SelectMany(e => e.MemberNames).ToList();
        members.ShouldContain("name");
        members.ShouldContain("slug");
        members.ShouldContain("displayOrder");
        (await WithDbAsync(db => db.Brands.CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task Duplicate_name_is_409_even_when_case_differs()
    {
        await CreateBrandAsync("Toyota", "toyota");

        var ex = await Should.ThrowAsync<BusinessException>(() => CreateBrandAsync("  TOYOTA ", "toyota-2"));

        ex.Code.ShouldBe(CatalogErrorCodes.Duplicate);
        ex.Data["field"].ShouldBe("name");
        (await WithDbAsync(db => db.Brands.CountAsync())).ShouldBe(1);
        (await AuditEntriesAsync()).Count.ShouldBe(1);   // lần thất bại không để lại audit
    }

    [Fact]
    public async Task Duplicate_slug_is_409()
    {
        await CreateBrandAsync("Toyota", "toyota");

        var ex = await Should.ThrowAsync<BusinessException>(() => CreateBrandAsync("Honda", "toyota"));

        ex.Code.ShouldBe(CatalogErrorCodes.Duplicate);
        ex.Data["field"].ShouldBe("slug");
    }

    [Fact]
    public async Task Two_concurrent_creates_with_the_same_name_let_exactly_one_succeed()
    {
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async i =>
        {
            try
            {
                await CreateBrandAsync("Mazda", $"mazda-{i}");
                return (Ok: true, Code: (string?)null);
            }
            catch (BusinessException ex)
            {
                return (Ok: false, Code: ex.Code);
            }
        }));

        results.Count(r => r.Ok).ShouldBe(1);
        results.Single(r => !r.Ok).Code.ShouldBe(CatalogErrorCodes.Duplicate);
        (await WithDbAsync(db => db.Brands.CountAsync())).ShouldBe(1);
    }

    [Fact]
    public async Task Unknown_brand_is_404()
    {
        await Should.ThrowAsync<EntityNotFoundException>(() => AsAdminAsync(() => Brands.GetAsync(Guid.NewGuid())));
        await Should.ThrowAsync<EntityNotFoundException>(() => AsAdminAsync(() => Brands.UpdateAsync(Guid.NewGuid(), new SaveBrandInput { Name = "x", Slug = "x" })));
    }

    [Fact]
    public async Task Update_with_a_stale_concurrency_stamp_is_a_409_and_changes_nothing()
    {
        var created = await CreateBrandAsync("Honda", "honda");
        var first = await AsAdminAsync(() => Brands.UpdateAsync(created.Id, new SaveBrandInput
        {
            Name = "Honda VN", Slug = "honda", ConcurrencyStamp = created.ConcurrencyStamp,
        }));
        first.ConcurrencyStamp.ShouldNotBe(created.ConcurrencyStamp);

        await Should.ThrowAsync<AbpDbConcurrencyException>(() => AsAdminAsync(() => Brands.UpdateAsync(created.Id, new SaveBrandInput
        {
            Name = "Honda STALE", Slug = "honda", ConcurrencyStamp = created.ConcurrencyStamp,
        })));

        (await AsAdminAsync(() => Brands.GetAsync(created.Id))).Name.ShouldBe("Honda VN");
    }

    [Fact]
    public async Task Changing_the_slug_records_the_old_slug_in_history()
    {
        var created = await CreateBrandAsync("Kia", "kia");

        await AsAdminAsync(() => Brands.UpdateAsync(created.Id, new SaveBrandInput { Name = "Kia", Slug = "kia-motors" }));

        var history = await WithDbAsync(db => db.SlugHistories.AsNoTracking().ToListAsync());
        history.Count.ShouldBe(1);
        history[0].EntityType.ShouldBe(CatalogEntityTypes.Brand);
        history[0].ScopeId.ShouldBe(Guid.Empty);
        history[0].OldSlug.ShouldBe("kia");
        history[0].EntityId.ShouldBe(created.Id);
    }

    [Fact]
    public async Task Another_brand_cannot_take_a_slug_that_belongs_to_a_renamed_brand()
    {
        var kia = await CreateBrandAsync("Kia", "kia");
        await AsAdminAsync(() => Brands.UpdateAsync(kia.Id, new SaveBrandInput { Name = "Kia", Slug = "kia-motors" }));
        var other = await CreateBrandAsync("Other", "other");

        var ex = await Should.ThrowAsync<BusinessException>(() => AsAdminAsync(() => Brands.UpdateAsync(other.Id, new SaveBrandInput
        {
            Name = "Other", Slug = "kia",
        })));

        ex.Code.ShouldBe(CatalogErrorCodes.Duplicate);
        ex.Data["field"].ShouldBe("slug");
    }

    // ---------------------------------------------------------------- audit

    [Fact]
    public async Task Create_writes_exactly_one_audit_entry_with_actor_correlation_id_and_empty_before()
    {
        var created = await AsAdminAsync(
            () => Brands.CreateAsync(new SaveBrandInput { Name = "Ford", Slug = "ford" }),
            correlationId: "cid-brand-1");

        var entries = await AuditEntriesAsync(created.Id.ToString());

        entries.Count.ShouldBe(1);
        entries[0].Action.ShouldBe(AuditActions.Create);
        entries[0].EntityType.ShouldBe(CatalogEntityTypes.Brand);
        entries[0].ActorId.ShouldBe(ActorId);
        entries[0].CorrelationId.ShouldBe("cid-brand-1");
        entries[0].OccurredAt.Kind.ShouldBe(DateTimeKind.Utc);

        using var changes = JsonDocument.Parse(entries[0].Changes);
        var name = changes.RootElement.EnumerateArray().Single(c => c.GetProperty("field").GetString() == "name");
        name.GetProperty("before").ValueKind.ShouldBe(JsonValueKind.Null);
        name.GetProperty("after").GetString().ShouldBe("Ford");
    }

    [Fact]
    public async Task Update_that_changes_nothing_writes_no_audit_entry()
    {
        var created = await CreateBrandAsync("Audi", "audi");

        await AsAdminAsync(() => Brands.UpdateAsync(created.Id, new SaveBrandInput { Name = "Audi", Slug = "audi" }));

        (await AuditEntriesAsync(created.Id.ToString())).Count.ShouldBe(1);   // chỉ còn bản ghi của lần tạo
    }

    [Fact]
    public async Task Update_writes_one_audit_entry_with_only_the_changed_fields()
    {
        var created = await CreateBrandAsync("BMW", "bmw");

        await AsAdminAsync(() => Brands.UpdateAsync(created.Id, new SaveBrandInput { Name = "BMW Group", Slug = "bmw", DisplayOrder = 0 }));

        var update = (await AuditEntriesAsync(created.Id.ToString())).Single(e => e.Action == AuditActions.Update);
        using var changes = JsonDocument.Parse(update.Changes);
        var fields = changes.RootElement.EnumerateArray().Select(c => c.GetProperty("field").GetString()).ToList();
        fields.ShouldBe(new[] { "name" });
    }

    [Fact]
    public async Task When_the_audit_write_fails_the_business_change_is_rolled_back()
    {
        FaultSwitch.FailWrites = true;

        await Should.ThrowAsync<InvalidOperationException>(() => AsAdminAsync(
            () => Brands.CreateAsync(new SaveBrandInput { Name = "Lexus", Slug = "lexus" })));

        FaultSwitch.FailWrites = false;
        (await WithDbAsync(db => db.Brands.CountAsync())).ShouldBe(0);   // Brand đã flush (autoSave) nhưng bị rollback
        (await AuditEntriesAsync()).ShouldBeEmpty();
    }

    // ---------------------------------------------------------------- danh sách

    [Fact]
    public async Task List_filters_by_status_and_accent_insensitive_keyword_and_pages()
    {
        await CreateBrandAsync("Đông Nam Auto", "dong-nam-auto");
        await CreateBrandAsync("Toyota", "toyota");
        await CreateBrandAsync("Honda", "honda", BrandStatus.Inactive);

        var byKeyword = await AsAdminAsync(() => Brands.GetListAsync(new BrandListInput { Keyword = "dong nam" }));
        byKeyword.Items.Select(b => b.Slug).ShouldBe(new[] { "dong-nam-auto" });

        var inactive = await AsAdminAsync(() => Brands.GetListAsync(new BrandListInput { Status = BrandStatus.Inactive }));
        inactive.Items.Select(b => b.Slug).ShouldBe(new[] { "honda" });

        var page = await AsAdminAsync(() => Brands.GetListAsync(new BrandListInput { Page = 2, PageSize = 2 }));
        page.TotalCount.ShouldBe(3);
        page.Items.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task List_rejects_bad_paging(int page, int pageSize)
    {
        await Should.ThrowAsync<AbpValidationException>(() => AsAdminAsync(
            () => Brands.GetListAsync(new BrandListInput { Page = page, PageSize = pageSize })));
    }

    // ---------------------------------------------------------------- quyền

    [Fact]
    public void Every_brand_operation_requires_the_catalog_edit_permission()
    {
        var attribute = typeof(BrandAppService).GetCustomAttribute<AuthorizeAttribute>();

        attribute.ShouldNotBeNull();
        attribute!.Policy.ShouldBe(AppPermissions.CatalogEdit);
        typeof(BrandAppService).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .ShouldAllBe(m => m.GetCustomAttribute<AllowAnonymousAttribute>() == null);
    }
}
