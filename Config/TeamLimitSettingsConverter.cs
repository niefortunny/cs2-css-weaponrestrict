using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using UmbrellaWeaponRestrict.Core;

namespace UmbrellaWeaponRestrict.Config;

/// <summary>
/// Writes each weapon on one line of the generated config: "awp": { "T": -1, "CT": -1 }.
/// </summary>
public sealed class TeamLimitSettingsConverter : JsonConverter<UmbrellaWeaponRestrictConfig.TeamLimitSettings>
{
    public override UmbrellaWeaponRestrictConfig.TeamLimitSettings Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("A weapon limit must look like { \"T\": -1, \"CT\": -1 }.");
        }

        UmbrellaWeaponRestrictConfig.TeamLimitSettings settings = new() { T = null, CT = null };
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            string name = reader.GetString() ?? string.Empty;
            if (!reader.Read() || reader.TokenType != JsonTokenType.Number)
            {
                throw new JsonException($"The weapon limit '{name}' must be a number.");
            }

            if (name.Equals("T", StringComparison.OrdinalIgnoreCase))
            {
                settings.T = reader.GetInt32();
            }
            else if (name.Equals("CT", StringComparison.OrdinalIgnoreCase))
            {
                settings.CT = reader.GetInt32();
            }
        }

        return settings;
    }

    public override void Write(Utf8JsonWriter writer, UmbrellaWeaponRestrictConfig.TeamLimitSettings value, JsonSerializerOptions options)
    {
        int t = value.T ?? RestrictionState.Unlimited;
        int ct = value.CT ?? RestrictionState.Unlimited;
        writer.WriteRawValue(string.Create(CultureInfo.InvariantCulture, $"{{ \"T\": {t}, \"CT\": {ct} }}"));
    }
}
