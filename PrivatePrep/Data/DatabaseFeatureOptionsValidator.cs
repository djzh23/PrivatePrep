using Microsoft.Extensions.Options;

namespace PrivatePrep.Data;

public sealed class DatabaseFeatureOptionsValidator : IValidateOptions<DatabaseFeatureOptions>
{
    public ValidateOptionsResult Validate(string? name, DatabaseFeatureOptions options) =>
        ValidateOptionsResult.Success;
}
