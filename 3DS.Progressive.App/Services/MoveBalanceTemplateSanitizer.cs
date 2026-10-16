// Copied without behavioral changes from the Progressive sanitizer (f459e40).
#nullable disable
using System;
using System.Reflection;

public static class MoveBalanceTemplateSanitizer
{
    // When a template defines stat effects, each Balance Moves pass must replace
    // the previous effect slots instead of appending into the next empty slot.
    public static bool ShouldClearStatEffectsBeforeApply(object patch)
    {
        if (patch == null)
            return false;

        // These are the CSV columns/properties that imply "replace stat effects".
        // We intentionally avoid Power/Accuracy/PP/etc., so normal numeric edits
        // do not clear existing effects.
        string[] names =
        {
            "UserStat",
            "TargetStat",
            "Stat1",
            "Stat2",
            "Stat3",
            "UserStatChange",
            "TargetStatChange",
            "Stat1Change",
            "Stat2Change",
            "Stat3Change",
            "UserStatChance",
            "TargetStatChance",
            "Stat1Chance",
            "Stat2Chance",
            "Stat3Chance"
        };

        Type type = patch.GetType();

        foreach (string name in names)
        {
            object value;
            if (TryGetMemberValue(type, patch, name, out value) && HasMeaningfulValue(value))
                return true;
        }

        return false;
    }

    private static bool TryGetMemberValue(Type type, object instance, string name, out object value)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;

        PropertyInfo prop = type.GetProperty(name, flags);
        if (prop != null)
        {
            value = prop.GetValue(instance, null);
            return true;
        }

        FieldInfo field = type.GetField(name, flags);
        if (field != null)
        {
            value = field.GetValue(instance);
            return true;
        }

        value = null;
        return false;
    }

    private static bool HasMeaningfulValue(object value)
    {
        if (value == null)
            return false;

        string text = Convert.ToString(value);
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // Nullable<T> boxes to T when it has a value, so any boxed numeric here
        // represents a real CSV value that was provided by the template.
        return true;
    }
}
