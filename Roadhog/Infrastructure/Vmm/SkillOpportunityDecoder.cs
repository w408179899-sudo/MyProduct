using System.Globalization;
using Roadhog.Core.Model;

namespace Roadhog.Infrastructure.Vmm;

/// <summary>
/// Read-only experimental adapter for the special-opportunity animation emitted
/// by UIItemSlot::Draw in the supplied 2026-09-09 layout. Ordinary slots have no
/// demonstrated availability source and are explicitly unsupported. This is
/// separate from the existing quickbar binding decoder.
/// </summary>
internal sealed class SkillOpportunityDecoder(
    Func<ulong, int, byte[]> read,
    Func<uint, bool> supportsSpecialOpportunity,
    Func<IReadOnlyList<(ulong Address, int Size)>, IReadOnlyList<byte[]>>? readMany = null)
{
    private sealed class OpportunityScopeChangedException(string message) : IOException(message);

    public SkillOpportunityRead Read(ulong module, ulong actor, bool includeCombatState = false)
    {
        try
        {
            var guards = new List<(ulong Address, byte[] Bytes, int ScopeBytes)>();
            var prefetched = new Dictionary<(ulong Address, int Size), byte[]>();
            void Prefetch(IReadOnlyList<(ulong Address, int Size)> requests)
            {
                foreach (var request in requests)
                    if (request.Address < 0x10000 || request.Address >= 0x800000000000)
                        throw new InvalidDataException("Invalid opportunity batch address.");
                IReadOnlyList<byte[]> values;
                if (readMany is not null) values = readMany(requests);
                else values = requests.Select(request =>
                {
                    try { return read(request.Address, request.Size); }
                    catch (Exception ex) when (ex is IOException or InvalidDataException) { return Array.Empty<byte>(); }
                }).ToArray();
                if (values.Count != requests.Count) throw new InvalidDataException("Incomplete opportunity batch.");
                for (var i = 0; i < requests.Count; i++) prefetched[requests[i]] = values[i] ?? Array.Empty<byte>();
            }
            byte[] Bytes(ulong address, int size)
            {
                if (address < 0x10000 || address >= 0x800000000000)
                    throw new InvalidDataException("Invalid opportunity capture address.");
                var bytes = prefetched.TryGetValue((address, size), out var value) ? value : read(address, size);
                if (bytes.Length != size) throw new InvalidDataException("Incomplete opportunity capture.");
                return bytes;
            }
            byte[] Guard(ulong address, int size, int scopeBytes = 0)
            {
                var bytes = Bytes(address, size);
                guards.Add((address, bytes, scopeBytes));
                return bytes;
            }
            ulong Pointer(ulong address)
            {
                var pointer = BitConverter.ToUInt64(Guard(address, 8, 8));
                if (pointer < 0x10000 || pointer >= 0x800000000000)
                    throw new InvalidDataException("Invalid opportunity slot pointer.");
                return pointer;
            }

            var anchors = new List<(ulong Address, int Size)> { (actor + 0x2C, 4), (module + 0xD4AE0C, 4), (module + 0x6E2180, 8) };
            if (includeCombatState) anchors.AddRange(new (ulong Address, int Size)[]
                { (module + 0xD6CB08, 4), (module + 0xD71BB4, 20), (actor + 0x358, 4) });
            Prefetch(anchors);
            var serverId = BitConverter.ToUInt32(Guard(actor + 0x2C, 4, 4));
            if (serverId == 0) throw new InvalidDataException("Opportunity actor has no stable identity.");
            var page = BitConverter.ToInt32(Guard(module + 0xD4AE0C, 4, 4));
            if (page is < 0 or >= 10) throw new InvalidDataException("Invalid opportunity page.");
            var dialogIds = Guard(module + 0x6E2180, 8, 8);
            var layoutRequests = new List<(ulong Address, int Size)> { (module + 0xD61260 + (ulong)page * 768, 384) };
            for (var bar = 0; bar < 2; bar++)
            {
                var dialogId = BitConverter.ToUInt32(dialogIds, bar * 4);
                if (dialogId > 0x19D) throw new InvalidDataException("Invalid opportunity dialog.");
                layoutRequests.Add((module + 0xD63990 + dialogId * 8, 8));
            }
            Prefetch(layoutRequests);
            var table = Guard(module + 0xD61260 + (ulong)page * 768, 384);
            var panelRequests = new List<(ulong Address, int Size)>();
            for (var bar = 0; bar < 2; bar++)
            {
                var panel = Pointer(layoutRequests[bar + 1].Address);
                panelRequests.AddRange(new (ulong Address, int Size)[] { (panel + 1344, 4), (panel + 0x28, 4), (panel + 1240, 96) });
            }
            Prefetch(panelRequests);
            var bindingRequests = new List<(ulong Address, int Size)>(24);
            for (var bar = 0; bar < 2; bar++)
            {
                var pointers = Bytes(panelRequests[bar * 3 + 2].Address, 96);
                for (var slot = 0; slot < 12; slot++)
                {
                    var control = BitConverter.ToUInt64(pointers, slot * 8);
                    if (control < 0x10000 || control >= 0x800000000000) throw new InvalidDataException("Invalid opportunity control pointer.");
                    bindingRequests.Add((control + 952, 12));
                }
            }
            Prefetch(bindingRequests);
            var stateRequests = new List<(ulong Address, int Size)> { (actor + 804, 8) };
            foreach (var bindingRequest in bindingRequests)
            {
                var binding = Bytes(bindingRequest.Address, 12);
                if (BitConverter.ToUInt32(binding, 4) != 21 || !supportsSpecialOpportunity(BitConverter.ToUInt32(binding, 8))) continue;
                var control = bindingRequest.Address - 952;
                stateRequests.AddRange(new (ulong Address, int Size)[] { (control + 0x28, 4), (control + 984, 4) });
            }
            Prefetch(stateRequests);
            var slots = new List<SkillOpportunitySlotObservation>(24);
            var issues = new List<string>();
            var presentationInactive = false;
            var bindingParts = new List<string>(25) { page.ToString(CultureInfo.InvariantCulture) };
            var layoutParts = new List<string>(26);
            ushort playerEntityId = 0;
            ushort? targetEntityId = null;
            uint? targetServerId = null, currentHp = null, maxHp = null, currentMp = null, maxMp = null;
            ushort? currentDp = null;
            if (includeCombatState)
            {
                var entities = Guard(module + 0xD6CB08, 4, 4);
                playerEntityId = BitConverter.ToUInt16(entities);
                if (playerEntityId == 0) throw new OpportunityScopeChangedException("Opportunity character is no longer active.");
                var resources = prefetched[(module + 0xD71BB4, 20)];
                if (resources.Length >= 8)
                {
                    var max = BitConverter.ToUInt32(resources);
                    var current = BitConverter.ToUInt32(resources, 4);
                    if (max > 0 && current <= max) { maxHp = max; currentHp = current; }
                }
                if (!maxHp.HasValue) issues.Add("Incomplete or invalid opportunity HP pair.");
                if (resources.Length >= 16)
                {
                    var max = BitConverter.ToUInt32(resources, 8);
                    var current = BitConverter.ToUInt32(resources, 12);
                    if (current <= max) { maxMp = max; currentMp = current; }
                }
                if (!maxMp.HasValue) issues.Add("Incomplete or invalid opportunity MP pair.");
                if (resources.Length == 20) currentDp = BitConverter.ToUInt16(resources, 18);
                else issues.Add("Incomplete opportunity DP field.");
                try
                {
                    var target = BitConverter.ToUInt16(entities, 2);
                    var targetServer = BitConverter.ToUInt32(Guard(actor + 0x358, 4, 4));
                    if ((target == 0) != (targetServer == 0)) throw new InvalidDataException("Opportunity target identities disagree.");
                    targetEntityId = target;
                    targetServerId = targetServer;
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException) { issues.Add(ex.Message); }
            }
            for (var bar = 0; bar < 2; bar++)
            {
                var dialogId = BitConverter.ToUInt32(dialogIds, bar * 4);
                if (dialogId > 0x19D) throw new InvalidDataException("Invalid opportunity dialog.");
                var panel = Pointer(module + 0xD63990 + dialogId * 8);
                layoutParts.Add(panel.ToString("X", CultureInfo.InvariantCulture));
                if (BitConverter.ToUInt32(Guard(panel + 1344, 4, 4)) != bar)
                    throw new InvalidDataException("Unexpected opportunity bar kind.");
                var panelVisible = (BitConverter.ToUInt32(Guard(panel + 0x28, 4, 4)) & 1) != 0;
                var pointers = Guard(panel + 1240, 96, 96);
                for (var slot = 0; slot < 12; slot++)
                {
                    var control = BitConverter.ToUInt64(pointers, slot * 8);
                    if (control < 0x10000 || control >= 0x800000000000)
                        throw new InvalidDataException("Invalid opportunity control pointer.");
                    layoutParts.Add(control.ToString("X", CultureInfo.InvariantCulture));
                    var binding = Guard(control + 952, 12, 8);
                    var baseId = BitConverter.ToUInt32(binding);
                    var type = BitConverter.ToUInt32(binding, 4);
                    var effectiveId = BitConverter.ToUInt32(binding, 8);
                    var tableOffset = (bar * 12 + slot) * 16;
                    var tableType = BitConverter.ToUInt32(table, tableOffset);
                    var tableId = BitConverter.ToUInt32(table, tableOffset + 4);
                    if (type > 55 || type != tableType ||
                        type == 21 && (baseId == 0 || baseId != tableId || effectiveId == 0))
                        throw new InvalidDataException("Opportunity binding table and display disagree.");
                    var isSkill = type == 21;
                    var supported = isSkill && supportsSpecialOpportunity(effectiveId);
                    bool? usable = null;
                    if (supported)
                    {
                        try
                        {
                            var visible = (BitConverter.ToUInt32(Guard(control + 0x28, 4, 4)) & 1) != 0;
                            if (!panelVisible || !visible)
                            {
                                presentationInactive = true;
                                throw new InvalidDataException("Opportunity slot is not being rendered.");
                            }
                            var timer = BitConverter.ToSingle(Bytes(control + 984, 4));
                            if (!float.IsFinite(timer) || timer < 0)
                                throw new InvalidDataException("Invalid opportunity animation timer.");
                            usable = timer > 0;
                        }
                        catch (Exception ex) when (ex is IOException or InvalidDataException) { issues.Add(ex.Message); }
                    }
                    slots.Add(new((SkillQuickbar)bar, slot, type, isSkill ? baseId : 0,
                        isSkill ? effectiveId : 0, supported, usable));
                    bindingParts.Add(string.Create(CultureInfo.InvariantCulture,
                        $"{bar}:{slot}:{type}:{(isSkill ? baseId : 0)}"));
                }
            }

            uint? releaseId = null;
            uint? releaseTime = null;
            byte[]? release = null;
            try
            {
                release = Bytes(actor + 804, 8);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException) { issues.Add(ex.Message); }
            var verificationRequests = guards.Select(g => (g.Address, Size: g.Bytes.Length)).Distinct().ToList();
            verificationRequests.Add((actor + 804, 8));
            Prefetch(verificationRequests);
            foreach (var guard in guards)
            {
                var after = Bytes(guard.Address, guard.Bytes.Length);
                if (guard.Bytes.SequenceEqual(after)) continue;
                if (guard.ScopeBytes > 0 && !guard.Bytes.AsSpan(0, guard.ScopeBytes).SequenceEqual(after.AsSpan(0, guard.ScopeBytes)))
                    throw new OpportunityScopeChangedException("Opportunity page, binding, presentation or role changed during capture.");
                throw new InvalidDataException("Opportunity display changed during capture.");
            }
            try
            {
                var after = Bytes(actor + 804, 8);
                if (release is null || !release.SequenceEqual(after)) throw new InvalidDataException("Release identity changed during capture.");
                releaseId = BitConverter.ToUInt32(after);
                releaseTime = BitConverter.ToUInt32(after, 4);
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException) { issues.Add(ex.Message); }

            return new(presentationInactive ? SkillOpportunityReadCompleteness.Failed :
                    issues.Count == 0 ? SkillOpportunityReadCompleteness.Complete : SkillOpportunityReadCompleteness.Partial,
                page, string.Join(';', bindingParts), serverId, slots.AsReadOnly(),
                releaseId, releaseTime, issues.Count == 0 ? null : string.Join("; ", issues),
                string.Join(';', layoutParts), presentationInactive, PlayerEntityId: playerEntityId,
                TargetEntityId: targetEntityId, TargetServerObjectId: targetServerId, CurrentHp: currentHp,
                MaxHp: maxHp, CurrentMp: currentMp, MaxMp: maxMp, CurrentDp: currentDp);
        }
        catch (OpportunityScopeChangedException ex) { return SkillOpportunityRead.Failed(ex.Message, scopeChanged: true); }
        catch (Exception ex) when (ex is IOException or InvalidDataException) { return SkillOpportunityRead.Failed(ex.Message); }
        catch (ArgumentException ex) { return SkillOpportunityRead.Failed(ex.Message); }
    }
}
