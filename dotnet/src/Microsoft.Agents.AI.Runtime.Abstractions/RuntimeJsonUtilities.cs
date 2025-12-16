// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Microsoft.Agents.AI.Runtime.Abstractions;

/// <summary>
/// Provides utility methods and configurations for JSON serialization operations within the Runtime.
/// </summary>
public static partial class RuntimeJsonUtilities
{
/// <summary>
/// Gets the default <see cref="JsonSerializerOptions"/> instance used for JSON serialization operations of runtime types.
/// </summary>
/// <remarks>
/// <para>
/// For Native AOT or applications disabling <see cref="JsonSerializer.IsReflectionEnabledByDefault"/>, this instance
/// includes source generated contracts for all common exchange types contained in this library.
/// </para>
/// <para>
/// It additionally turns on the following settings:
/// <list type="number">
/// <item>Enables <see cref="JsonSerializerDefaults.Web"/> defaults.</item>
/// <item>Enables <see cref="System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull"/> as the default ignore condition for properties.</item>
/// <item>Enables <see cref="System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString"/> as the default number handling for number types.</item>
/// <item>Enables <see cref="System.Text.Json.Serialization.JsonStringEnumConverter"/> for enum serialization.</item>
/// </list>
/// </para>
/// </remarks>
    public static JsonSerializerOptions DefaultOptions { get; } = CreateDefaultOptions();

    /// <summary>
    /// Creates and configures the default JSON serialization options for runtime types.
    /// </summary>
    /// <returns>The configured options.</returns>
    [UnconditionalSuppressMessage("ReflectionAnalysis", "IL3050:RequiresDynamicCode", Justification = "Converter is guarded by IsReflectionEnabledByDefault check.")]
    [UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access", Justification = "Converter is guarded by IsReflectionEnabledByDefault check.")]
    private static JsonSerializerOptions CreateDefaultOptions()
    {
        JsonSerializerOptions options = new(RuntimeJsonContext.Default.Options);
        options.TypeInfoResolverChain.Add(Microsoft.Extensions.AI.AIJsonUtilities.DefaultOptions.TypeInfoResolver!);
        options.MakeReadOnly();
        return options;
    }
}
