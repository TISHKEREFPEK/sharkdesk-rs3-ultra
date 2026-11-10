using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SharkDesk;

public sealed class AdvancedSettings
{
    public int? SensorMode { get; set; }
    public bool? Competitive { get; set; }
    public int? CompetitiveSeconds { get; set; }
    public int? Angle { get; set; }
    public bool? AngleEnabled { get; set; }
    public bool? Fps20K { get; set; }
    public bool? LongRange { get; set; }
    public int? WheelDebounce { get; set; }
    public int? ReleaseDebounce { get; set; }
    public void Validate()
    {
        if (SensorMode is < 0 or > 1 || Angle is < -30 or > 30 ||
            CompetitiveSeconds is < 10 or > 2550 || (CompetitiveSeconds.HasValue && CompetitiveSeconds.Value % 10 != 0) ||
            WheelDebounce is < 0 or > 255 || ReleaseDebounce is < 0 or > 15)
            throw new ArgumentException("Проверь режим сенсора, угол (−30…30°), время и задержки.");
    }
}

public sealed class MacroStep
{
    public string Key { get; set; } = "A";
    public bool Down { get; set; } = true;
    public int Delay { get; set; } = 10;
}

public sealed class MouseMacro
{
    public string Name { get; set; } = "Макрос";
    public List<MacroStep> Steps { get; set; } = new();
}

public sealed class ButtonAssignment
{
    public int Index { get; set; }
    public int Type { get; set; } = 1;
    public int Parameter { get; set; }
    public string Shortcut { get; set; } = "";
    public MouseMacro? Macro { get; set; }
    public static string Label(int index) => new[] { "Левая кнопка", "Правая кнопка", "Колесо (нажатие)", "Боковая «Назад»", "Боковая «Вперёд»" }[index];
}

// Packet layouts checked against the official controlhub.top/AttackShark driver.
public static class HubCodec
{
    public static byte Checksum(IEnumerable<byte> bytes) => unchecked((byte)(85 - bytes.Sum(x => (int)x)));
    public static void Check(ReadOnlySpan<byte> data)
    {
        if ((data.ToArray().Sum(x => (int)x) & 255) != 85) throw new IOException("Неверная контрольная сумма данных мыши.");
    }
    public static byte[] EncodeDpiAxes(int x, int y)
    {
        var spec = MouseSpec.Find(124, 14)!;
        if (!spec.ValidDpi(x) || !spec.ValidDpi(y)) throw new ArgumentException("DPI X/Y должен быть от 1 до 52 000; выше 42 000 — чётный.");
        int a = (x > 42000 ? x / 2 : x) - 1, b = (y > 42000 ? y / 2 : y) - 1;
        byte[] r = { (byte)a, (byte)(a >> 8), (byte)b, (byte)(b >> 8), (byte)((x > 42000 ? 1 : 0) | (y > 42000 ? 16 : 0)), 0 };
        r[5] = Checksum(r);
        return r;
    }
    public static int DecodeDpiAxis(ReadOnlySpan<byte> data, bool y)
    {
        if (data.Length != 6) throw new IOException("Неверный размер записи DPI.");
        Check(data);
        int shift = y ? 4 : 0, i = y ? 2 : 0, flags = data[4] >> shift;
        int value = (data[i] | data[i + 1] << 8 | ((flags >> 2 & 3) << 16)) + 1;
        if ((flags & 1) != 0) value *= 2;
        if ((flags & 2) != 0) value *= 2;
        return value;
    }
    public static byte[] EncodeColor(string color)
    {
        if (color == null || color.Length != 7 || color[0] != '#') throw new ArgumentException("Цвет задаётся как #RRGGBB.");
        byte[] r;
        try { r = Convert.FromHexString(color[1..]).Concat(new byte[1]).ToArray(); }
        catch (FormatException) { throw new ArgumentException("Цвет задаётся как #RRGGBB."); }
        r[3] = Checksum(r); return r;
    }
    public static string DecodeColor(ReadOnlySpan<byte> data) { Check(data); return "#" + Convert.ToHexString(data[..3]); }
    public static byte[] EncodeButton(ButtonAssignment assignment)
    {
        if (assignment.Index is < 0 or > 4 || assignment.Type is < 0 or > 15 || assignment.Parameter is < 0 or > 65535)
            throw new ArgumentException("Недопустимое назначение кнопки.");
        byte[] data = { (byte)assignment.Type, (byte)(assignment.Parameter >> 8), (byte)assignment.Parameter, 0 };
        data[3] = Checksum(data); return data;
    }
    public static ButtonAssignment DecodeButton(int index, ReadOnlySpan<byte> data)
    {
        Check(data); return new() { Index = index, Type = data[0], Parameter = data[1] << 8 | data[2] };
    }
    public static byte[] EncodeShortcut(string chord)
    {
        var keys = chord.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(HidKeys.Parse).ToArray();
        if (keys.Length is < 1 or > 5 || keys.Distinct().Count() != keys.Length) throw new ArgumentException("Комбинация: от 1 до 5 разных клавиш, например Ctrl+Shift+A.");
        var result = new List<byte> { (byte)(keys.Length * 2) };
        foreach (var key in keys) result.AddRange(new byte[] { (byte)(0x80 | key.Type), (byte)key.Value, (byte)(key.Value >> 8) });
        foreach (var key in keys.Reverse()) result.AddRange(new byte[] { (byte)(0x40 | key.Type), (byte)key.Value, (byte)(key.Value >> 8) });
        result.Add(Checksum(result)); return result.ToArray();
    }
    public static string DecodeShortcut(ReadOnlySpan<byte> data)
    {
        int count = data[0];
        if (count == 0 || count == 255) return "";
        if (count > 10 || count % 2 != 0 || count * 3 + 2 > data.Length) throw new IOException("Неверная комбинация клавиш.");
        Check(data[..(count * 3 + 2)]);
        var keys = new List<string>();
        for (int i = 0; i < count / 2; i++) keys.Add(HidKeys.Name(data[1 + i * 3] & 15, data[2 + i * 3] | data[3 + i * 3] << 8));
        return string.Join('+', keys);
    }
    public static byte[] EncodeMacro(MouseMacro macro)
    {
        byte[] name = Encoding.UTF8.GetBytes(macro.Name ?? "");
        if (name.Length is < 1 or > 30 || macro.Steps == null || macro.Steps.Count is < 1 or > 70)
            throw new ArgumentException("Имя макроса — до 30 байт UTF-8 (обычно 15 русских букв); от 1 до 70 событий.");
        var data = Enumerable.Repeat((byte)255, 33 + macro.Steps.Count * 5).ToArray();
        data[0] = (byte)name.Length; name.CopyTo(data, 1); data[31] = (byte)macro.Steps.Count;
        for (int i = 0; i < macro.Steps.Count; i++) {
            var step = macro.Steps[i]; var key = HidKeys.Parse(step.Key);
            if (step.Delay is < 0 or > 65535) throw new ArgumentException("Задержка события — от 0 до 65 535 мс.");
            int p = 32 + i * 5;
            data[p] = (byte)((step.Down ? 0x80 : 0x40) | key.Type);
            data[p + 1] = (byte)key.Value; data[p + 2] = (byte)(key.Value >> 8);
            data[p + 3] = (byte)(step.Delay >> 8); data[p + 4] = (byte)step.Delay;
        }
        data[^1] = Checksum(data.Skip(31).Take(data.Length - 32));
        return data;
    }
    public static MouseMacro? DecodeMacro(ReadOnlySpan<byte> data)
    {
        if (data[0] is 0 or 255) return null;
        int nameLength = data[0], count = data[31], size = 33 + count * 5;
        if (nameLength > 30 || count > 70 || size > data.Length) throw new IOException("Неверный формат макроса в мыши.");
        Check(data.Slice(31, size - 31));
        var macro = new MouseMacro { Name = Encoding.UTF8.GetString(data.Slice(1, nameLength)) };
        for (int i = 0; i < count; i++) {
            int p = 32 + i * 5;
            macro.Steps.Add(new() { Key = HidKeys.Name(data[p] & 15, data[p + 1] | data[p + 2] << 8), Down = (data[p] >> 6) == 2, Delay = data[p + 3] << 8 | data[p + 4] });
        }
        return macro;
    }
    public static void ValidateButtons(ButtonAssignment[] buttons)
    {
        if (buttons.Length != 5 || !buttons.Select(b => b.Index).OrderBy(x => x).SequenceEqual(Enumerable.Range(0, 5)))
            throw new ArgumentException("Профиль должен содержать пять кнопок RS3 Ultra.");
        if (!buttons.Any(b => b.Type == 1 && b.Parameter == 0x0100)) throw new ArgumentException("Сохрани обычный левый клик хотя бы на одной кнопке.");
        foreach (var b in buttons) {
            EncodeButton(b);
            if (b.Type == 5) EncodeShortcut(b.Shortcut);
            if (b.Type == 6) {
                if ((b.Parameter >> 8) != b.Index || (b.Parameter & 255) is 0 or 251 or 252)
                    throw new ArgumentException("Неверный слот или режим повтора макроса.");
                EncodeMacro(b.Macro ?? throw new ArgumentException("Для кнопки не выбран макрос."));
            }
        }
    }
}

public readonly record struct HidKey(int Type, int Value);
public static class HidKeys
{
    private static readonly Dictionary<string, HidKey> Keys = Build();
    private static Dictionary<string, HidKey> Build()
    {
        var d = new Dictionary<string, HidKey>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < 26; i++) d[((char)('A' + i)).ToString()] = new(1, i + 4);
        for (int i = 1; i <= 9; i++) d[i.ToString()] = new(1, 29 + i);
        d["0"] = new(1, 39);
        for (int i = 1; i <= 12; i++) d["F" + i] = new(1, 57 + i);
        for (int i = 13; i <= 24; i++) d["F" + i] = new(1, 104 + i - 13);
        string[] normal = { "Enter", "Escape", "Backspace", "Tab", "Space", "Minus", "Plus", "LeftBracket", "RightBracket", "Backslash", "NonUS", "Semicolon", "Quote", "Tilde", "Comma", "Period", "Slash", "CapsLock" };
        for (int i = 0; i < normal.Length; i++) d[normal[i]] = new(1, 40 + i);
        string[] nav = { "PrintScreen", "ScrollLock", "Pause", "Insert", "Home", "PageUp", "Delete", "End", "PageDown", "Right", "Left", "Down", "Up", "NumLock", "Divide", "Multiply", "Subtract", "Add", "NumEnter", "Num1", "Num2", "Num3", "Num4", "Num5", "Num6", "Num7", "Num8", "Num9", "Num0", "Decimal" };
        for (int i = 0; i < nav.Length; i++) d[nav[i]] = new(1, 70 + i);
        string[] modifiers = { "Ctrl", "Shift", "Alt", "Win", "RCtrl", "RShift", "RAlt", "RWin" };
        for (int i = 0; i < modifiers.Length; i++) d[modifiers[i]] = new(0, 1 << i);
        d["LCtrl"] = d["Ctrl"]; d["LShift"] = d["Shift"]; d["LAlt"] = d["Alt"]; d["LWin"] = d["Win"];
        d["LeftCtrl"] = d["Ctrl"]; d["RightCtrl"] = d["RCtrl"]; d["LeftShift"] = d["Shift"]; d["RightShift"] = d["RShift"]; d["LeftAlt"] = d["Alt"]; d["RightAlt"] = d["RAlt"];
        d["LWin"] = d["Win"]; d["Return"] = d["Enter"]; d["Back"] = d["Backspace"]; d["Esc"] = d["Escape"];
        d["MouseLeft"] = new(4, 1); d["MouseRight"] = new(4, 2); d["MouseMiddle"] = new(4, 4); d["MouseBack"] = new(4, 8); d["MouseForward"] = new(4, 16);
        d["PlayPause"] = new(2, 0xCD); d["NextTrack"] = new(2, 0xB5); d["PreviousTrack"] = new(2, 0xB6); d["Mute"] = new(2, 0xE2); d["VolumeUp"] = new(2, 0xE9); d["VolumeDown"] = new(2, 0xEA); d["Calculator"] = new(2, 0x192);
        return d;
    }
    public static HidKey Parse(string name)
    {
        if (name != null && Keys.TryGetValue(name.Trim(), out var key)) return key;
        if (name != null && name.StartsWith("HID:", StringComparison.OrdinalIgnoreCase)) {
            var parts = name.Split(':');
            if (parts.Length == 3 && int.TryParse(parts[1], out int type) && int.TryParse(parts[2], out int value) && type is >= 0 and <= 15 && value is >= 0 and <= 65535) return new(type, value);
        }
        throw new ArgumentException("Неизвестная клавиша «" + name + "». Например: A, F1, Ctrl, Space, MouseLeft, VolumeUp.");
    }
    public static string Name(int type, int value) => Keys.FirstOrDefault(kv => kv.Value == new HidKey(type, value)).Key ?? $"HID:{type}:{value}";
}
