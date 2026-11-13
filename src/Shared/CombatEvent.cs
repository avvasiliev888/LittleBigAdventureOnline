using System;
using System.IO;

namespace Shared
{
    /// <summary>Вид визуального эффекта атаки. Сервер выбирает его по оружию атакующего.</summary>
    public enum CombatStyle : byte
    {
        /// <summary>Ближний бой: взмах мечом, топором или кинжалом.</summary>
        Melee = 0,

        /// <summary>Лук: летящая стрела.</summary>
        Arrow = 1,

        /// <summary>Посох: магический шар.</summary>
        Magic = 2,
    }

    /// <summary>
    /// Событие атаки (Protocol v11). Нужно клиентам, чтобы проигрывать замах и снаряд
    /// у чужих сущностей: серверный снапшот содержит только результат (HP/кулдаун),
    /// но не момент и вид атаки. Событие чисто презентационное и не влияет на урон.
    /// </summary>
    public readonly struct CombatEvent
    {
        /// <summary>Создаёт событие атаки.</summary>
        /// <param name="attackerEntityId">Сущность, которая атакует; положительная.</param>
        /// <param name="targetEntityId">Цель; 0 — удар в пустоту.</param>
        /// <param name="abilityId">Способность из конфига.</param>
        /// <param name="tick">Серверный тик, на котором атака применена.</param>
        /// <param name="style">Визуальный стиль атаки.</param>
        public CombatEvent(uint attackerEntityId, uint targetEntityId, uint abilityId, uint tick, CombatStyle style)
        {
            AttackerEntityId = attackerEntityId;
            TargetEntityId = targetEntityId;
            AbilityId = abilityId;
            Tick = tick;
            Style = style;
        }

        /// <summary>Кто атакует.</summary>
        public uint AttackerEntityId { get; }

        /// <summary>Цель атаки; 0 — цель отсутствует.</summary>
        public uint TargetEntityId { get; }

        /// <summary>Способность из конфига.</summary>
        public uint AbilityId { get; }

        /// <summary>Серверный тик применения.</summary>
        public uint Tick { get; }

        /// <summary>Визуальный стиль.</summary>
        public CombatStyle Style { get; }
    }

    /// <summary>
    /// Кодек сообщения <see cref="MessageType.CombatEvent"/>: attacker, target, ability, tick, style.
    /// Формат фиксирован Protocol v11.
    /// </summary>
    public static class CombatEventCodec
    {
        /// <summary>Размер тела сообщения: 4 поля по 4 байта + байт стиля.</summary>
        public const int Size = 17;

        /// <summary>Кодирует событие атаки.</summary>
        /// <param name="evt">Событие.</param>
        /// <returns>Тело сообщения.</returns>
        public static byte[] Encode(CombatEvent evt)
        {
            var buffer = new byte[Size];
            buffer[0] = (byte)evt.AttackerEntityId;
            buffer[1] = (byte)(evt.AttackerEntityId >> 8);
            buffer[2] = (byte)(evt.AttackerEntityId >> 16);
            buffer[3] = (byte)(evt.AttackerEntityId >> 24);
            buffer[4] = (byte)evt.TargetEntityId;
            buffer[5] = (byte)(evt.TargetEntityId >> 8);
            buffer[6] = (byte)(evt.TargetEntityId >> 16);
            buffer[7] = (byte)(evt.TargetEntityId >> 24);
            buffer[8] = (byte)evt.AbilityId;
            buffer[9] = (byte)(evt.AbilityId >> 8);
            buffer[10] = (byte)(evt.AbilityId >> 16);
            buffer[11] = (byte)(evt.AbilityId >> 24);
            buffer[12] = (byte)evt.Tick;
            buffer[13] = (byte)(evt.Tick >> 8);
            buffer[14] = (byte)(evt.Tick >> 16);
            buffer[15] = (byte)(evt.Tick >> 24);
            buffer[16] = (byte)evt.Style;
            return buffer;
        }

        /// <summary>Декодирует событие атаки.</summary>
        /// <param name="payload">Тело сообщения.</param>
        /// <param name="evt">Событие при успехе.</param>
        /// <returns><c>true</c>, если формат корректен.</returns>
        public static bool TryDecode(ReadOnlySpan<byte> payload, out CombatEvent evt)
        {
            evt = default;
            if (payload.Length != Size)
            {
                return false;
            }

            var attacker = (uint)(payload[0] | (payload[1] << 8) | (payload[2] << 16) | (payload[3] << 24));
            var target = (uint)(payload[4] | (payload[5] << 8) | (payload[6] << 16) | (payload[7] << 24));
            var ability = (uint)(payload[8] | (payload[9] << 8) | (payload[10] << 16) | (payload[11] << 24));
            var tick = (uint)(payload[12] | (payload[13] << 8) | (payload[14] << 16) | (payload[15] << 24));
            var style = payload[16];
            if (attacker == 0 || style > (byte)CombatStyle.Magic)
            {
                return false;
            }

            evt = new CombatEvent(attacker, target, ability, tick, (CombatStyle)style);
            return true;
        }
    }
}
