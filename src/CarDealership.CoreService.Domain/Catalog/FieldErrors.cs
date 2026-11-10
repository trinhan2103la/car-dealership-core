using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Volo.Abp.Validation;

namespace CarDealership.CoreService.Catalog;

/// <summary>
/// Gom lỗi validation theo từng trường rồi ném MỘT <see cref="AbpValidationException"/> (HTTP 400, liệt kê đủ mọi
/// trường lỗi trong một phản hồi, Req 7.2 và 8.9). Tên trường dùng camelCase như JSON của DTO
/// (<c>name</c>, <c>referencePrice</c>, <c>extraSpecs[3].name</c>).
/// </summary>
public sealed class FieldErrors
{
    private readonly List<ValidationResult> _errors = new();

    public bool Any => _errors.Count > 0;

    public int Count => _errors.Count;

    public IReadOnlyList<ValidationResult> Errors => _errors;

    public IEnumerable<string> Members => _errors.SelectMany(e => e.MemberNames).Distinct();

    public FieldErrors Add(string member, string message)
    {
        _errors.Add(new ValidationResult(message, new[] { member }));
        return this;
    }

    public FieldErrors AddRange(FieldErrors other)
    {
        _errors.AddRange(other._errors);
        return this;
    }

    public void ThrowIfAny()
    {
        if (_errors.Count > 0)
        {
            throw new AbpValidationException(_errors.ToList());
        }
    }
}
