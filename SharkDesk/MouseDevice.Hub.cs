using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SharkDesk;

public sealed partial class MouseDevice
{
    public async Task<int?> ReadOnboardProfile()
    {
        try { int index = (await Ask(14))[5]; return index is >= 0 and <= 3 ? index : null; }
        catch (IOException) { return null; }
    }
    public async Task SelectOnboardProfile(int index)
    {
        if (index is < 0 or > 3 || !((await ReadOnboardProfile()).HasValue)) throw new ArgumentException("Встроенный профиль недоступен.");
        await Ask(15, 0, new[] { (byte)index });
        if (await ReadOnboardProfile() != index) throw new IOException("Мышь не подтвердила переключение профиля.");
    }
    private async Task<AdvancedSettings> ReadAdvanced()
    {
        byte[] sensor = await Read(181, 12), extra = await Read(225, 6);
        int? Value(byte[] data, int offset) => ((data[offset] + data[offset + 1]) & 255) == 85 ? data[offset] : null;
        var s = new AdvancedSettings {
            Competitive = Value(sensor, 0) is int state ? state == 1 : null,
            CompetitiveSeconds = Value(sensor, 2) * 10,
            SensorMode = Value(sensor, 4),
            Angle = Value(sensor, 8) is int angle ? (sbyte)angle : null,
            AngleEnabled = Value(sensor, 10) is int enabled ? enabled == 1 : null,
            Fps20K = Value(extra, 0) is int fps ? fps == 1 : null,
            WheelDebounce = Value(extra, 2), ReleaseDebounce = Value(extra, 4)
        };
        if (IsReceiver) {
            try { byte[] r = await Ask(23); if (r[5] <= 1) s.LongRange = r[5] == 1; }
            catch (IOException) { }
        }
        return s;
    }

    private async Task ApplyAdvanced(AdvancedSettings target, AdvancedSettings current)
    {
        target.Validate();
        async Task Set(int address, int? value, int? old) {
            if (!value.HasValue || value == old) return;
            if (!old.HasValue) throw new IOException("Прошивка не подтвердила поддержку настройки " + address + ".");
            await WriteVerified(address, PairBytes(value.Value));
        }
        int? Bit(bool? value) => value.HasValue ? (value.Value ? 1 : 0) : null;
        await Set(181, Bit(target.Competitive), Bit(current.Competitive));
        await Set(183, target.CompetitiveSeconds / 10, current.CompetitiveSeconds / 10);
        await Set(185, target.SensorMode, current.SensorMode);
        await Set(189, target.Angle.HasValue ? (byte)target.Angle.Value : null, current.Angle.HasValue ? (byte)current.Angle.Value : null);
        await Set(191, Bit(target.AngleEnabled), Bit(current.AngleEnabled));
        await Set(225, Bit(target.Fps20K), Bit(current.Fps20K));
        await Set(227, target.WheelDebounce, current.WheelDebounce);
        await Set(229, target.ReleaseDebounce, current.ReleaseDebounce);
        if (target.LongRange.HasValue && target.LongRange != current.LongRange) {
            if (!current.LongRange.HasValue || !IsReceiver) throw new IOException("Подключи беспроводной приёмник для режима дальней связи.");
            byte[] payload = new byte[10]; payload[0] = (byte)(target.LongRange.Value ? 1 : 0);
            await Ask(22, 0, payload);
            if ((await Ask(23))[5] != payload[0]) throw new IOException("Приёмник не подтвердил режим дальней связи.");
        }
    }

    internal async Task WriteVerified(int address, byte[] data)
    {
        for (int i = 0; i < data.Length; i += 10) {
            byte[] chunk = data.Skip(i).Take(Math.Min(10, data.Length - i)).ToArray();
            await Ask(7, address + i, chunk);
            if (!(await Read(address + i, chunk.Length)).SequenceEqual(chunk))
                throw new IOException("Мышь не подтвердила запись по адресу " + (address + i) + ". Перечитай настройки.");
        }
    }

    public async Task<ButtonAssignment[]> ReadButtons()
    {
        byte[] raw = await Read(96, 20);
        var result = Enumerable.Range(0, 5).Select(i => HubCodec.DecodeButton(i, raw.AsSpan(i * 4, 4))).ToArray();
        foreach (var button in result) {
            if (button.Type == 5) button.Shortcut = HubCodec.DecodeShortcut(await Read(256 + button.Index * 32, 32));
            if (button.Type == 6) {
                int slot = button.Parameter >> 8;
                if (slot is < 0 or > 4) throw new IOException("Неизвестный слот макроса.");
                button.Macro = HubCodec.DecodeMacro(await Read(768 + slot * 384, 384));
            }
        }
        return result;
    }

    public async Task ApplyButtons(ButtonAssignment[] target)
    {
        HubCodec.ValidateButtons(target);
        var current = await ReadButtons();
        // Write payloads first, then activate their mappings. Restore previous payloads on failure.
        var rollback = new List<(int Address, byte[] Data)>();
        try {
            foreach (var button in target.OrderBy(b => b.Index)) {
                int index = button.Index;
                if (button.Type == 5) await Put(256 + index * 32, HubCodec.EncodeShortcut(button.Shortcut));
                if (button.Type == 6) await Put(768 + index * 384, HubCodec.EncodeMacro(button.Macro!));
            }
            // Preserve a working left-click mapping throughout a multi-button remap.
            foreach (var button in target.OrderByDescending(b => b.Type == 1 && b.Parameter == 0x100))
                await Put(96 + button.Index * 4, HubCodec.EncodeButton(button));
        }
        catch (Exception failure) {
            try { foreach (var old in rollback.AsEnumerable().Reverse()) await WriteVerified(old.Address, old.Data); }
            catch (Exception restore) { throw new IOException("Запись прервана; восстановление также не удалось. Закрой приложение и открой официальный HUB.", new AggregateException(failure, restore)); }
            throw new IOException("Запись прервана. Предыдущие назначения восстановлены.", failure);
        }
        async Task Put(int address, byte[] bytes) {
            byte[] old = await Read(address, bytes.Length);
            if (old.SequenceEqual(bytes)) return;
            rollback.Add((address, old));
            await WriteVerified(address, bytes);
        }
    }

    // Read-only, human-inspectable backup; deliberately not an arbitrary flash-write interface.
    public async Task<Dictionary<string, string>> ReadBackup()
    {
        var result = new Dictionary<string, string> { ["0"] = Convert.ToHexString(await Read(0, 256)), ["6912"] = Convert.ToHexString(await Read(6912, 36)) };
        for (int i = 0; i < 5; i++) {
            result[(256 + i * 32).ToString()] = Convert.ToHexString(await Read(256 + i * 32, 32));
            result[(768 + i * 384).ToString()] = Convert.ToHexString(await Read(768 + i * 384, 384));
        }
        return result;
    }
}
