using System.Text.Json;
using SharkDesk;

if (args.Contains("--hardware-read")) {
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(50));
    try {
        using var device = await MouseDevice.Connect().WaitAsync(deadline.Token);
        Console.WriteLine("Connected: MID " + device.Spec.Mid);
        var settings = await device.ReadSettings().WaitAsync(deadline.Token);
        Console.WriteLine(JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        var buttons = await device.ReadButtons().WaitAsync(deadline.Token);
        Console.WriteLine(JsonSerializer.Serialize(buttons, new JsonSerializerOptions { WriteIndented = true }));
        var snapshot = new { model = device.Spec.Name, at = DateTimeOffset.UtcNow, settings, buttons, memory = await device.ReadBackup().WaitAsync(deadline.Token) };
        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SharkDesk-RS3-Ultra", "backups"); Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "before-adaptation-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json");
        File.WriteAllText(file, JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true })); Console.WriteLine("Backup: " + file);
    } catch (Exception error) { Console.WriteLine(error); Environment.ExitCode = 1; }
    return;
}

int checks = 0;
void Equal<T>(T actual, T expected, string label) { checks++; if (!Equals(actual, expected)) throw new Exception($"{label}: {actual} != {expected}"); }
void Reject(Action operation, string label) { checks++; try { operation(); } catch (ArgumentException) { return; } catch (IOException) { return; } throw new Exception("Expected rejection: " + label); }
for (int dpi = 1; dpi <= 52000; dpi += dpi >= 42000 ? 2 : 1) {
    byte[] data = HubCodec.EncodeDpiAxes(dpi, 52000);
    Equal(HubCodec.DecodeDpiAxis(data, false), dpi, "DPI X"); Equal(HubCodec.DecodeDpiAxis(data, true), 52000, "DPI Y");
}
Reject(() => HubCodec.EncodeDpiAxes(42001, 800), "odd extended DPI");
Reject(() => HubCodec.EncodeDpiAxes(52002, 800), "RS3 maximum");
Equal(Convert.ToHexString(HubCodec.EncodeDpiAxes(800, 2400)), "1F035F0900CB", "independent DPI axes fixture");
Equal(Convert.ToHexString(HubCodec.EncodeColor("#FF0000")), "FF000056", "red fixture");
Equal(HubCodec.DecodeColor(HubCodec.EncodeColor("#123456")), "#123456", "color");
Equal(Convert.ToHexString(HubCodec.EncodeShortcut("Ctrl+A")), "04800100810400410400400100C5", "shortcut fixture");
Equal(HubCodec.DecodeShortcut(HubCodec.EncodeShortcut("Ctrl+Shift+A")), "Ctrl+Shift+A", "shortcut round trip");
Equal(HubCodec.DecodeShortcut(HubCodec.EncodeShortcut("VolumeUp")), "VolumeUp", "media shortcut");
Reject(() => HubCodec.EncodeShortcut("Ctrl+Shift+Alt+Win+A+B"), "oversized shortcut");
var macro = new MouseMacro { Name = "Тест", Steps = new() { new() { Key = "A", Down = true, Delay = 20 }, new() { Key = "A", Down = false, Delay = 100 } } };
var encoded = HubCodec.EncodeMacro(macro); var decoded = HubCodec.DecodeMacro(encoded)!;
Equal(decoded.Name, macro.Name, "UTF8 name"); Equal(decoded.Steps.Count, 2, "event count"); Equal(decoded.Steps[1].Delay, 100, "event delay");
Equal(Convert.ToHexString(encoded.AsSpan(31)), "028104000014410400006411", "macro event fixture");
encoded[^1] ^= 1; Reject(() => HubCodec.DecodeMacro(encoded), "corrupt macro");
macro.Steps[0].Delay = 65536; Reject(() => HubCodec.EncodeMacro(macro), "delay overflow"); macro.Steps[0].Delay = 20;
macro.Name = new string('Я', 16); Reject(() => HubCodec.EncodeMacro(macro), "UTF8 name overflow");
macro.Name = "70 events"; macro.Steps = Enumerable.Range(0, 70).Select(i => new MacroStep { Key = "MouseLeft", Down = i % 2 == 0, Delay = 10 }).ToList();
Equal(HubCodec.EncodeMacro(macro).Length, 383, "maximum macro fits 384-byte bank");
macro.Steps.Add(new()); Reject(() => HubCodec.EncodeMacro(macro), "71st event");
var assignments = Enumerable.Range(0, 5).Select(i => new ButtonAssignment { Index = i, Type = 1, Parameter = 0x100 << i }).ToArray();
HubCodec.ValidateButtons(assignments); assignments[0].Type = 0; Reject(() => HubCodec.ValidateButtons(assignments), "remove last left click");
assignments[0].Type = 1;
for (int i = 0; i < 5; i++) { var b = HubCodec.DecodeButton(i, HubCodec.EncodeButton(assignments[i])); Equal(b.Parameter, assignments[i].Parameter, "button mapping"); }
new AdvancedSettings().Validate();
Reject(() => new AdvancedSettings { Angle = 31 }.Validate(), "angle bound");
Reject(() => new AdvancedSettings { CompetitiveSeconds = 11 }.Validate(), "ten-second units");
Console.WriteLine($"Passed {checks:N0} protocol checks.");
