using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NativeMobPreviewAudit;
using TooManyLags;

internal static class BridgeFieldSchemaAudit
{
    public static int Main()
    {
        try
        {
            List<string> lines = new List<string>();
            NativeSteeringPlan plan = new NativeSteeringPlan();
            lines.Add("COPIED METHODS " + plan.Copies);
            lines.Add("FIELD READ ADAPTERS " + plan.FieldReads);
            if (plan.FieldReads != 1) throw new Exception("Weapon cooldown field-read adapter missing.");
            foreach (MethodInfo method in plan.CopiedMethods)
                lines.Add(method.DeclaringType.FullName + "." + method.Name);
            lines.Add("AI FIELDS " + plan.AiFields.Length);
            foreach (FieldInfo field in plan.AiFields)
                lines.Add(field.DeclaringType.Name + "." + field.Name + " : " + field.FieldType.FullName);
            lines.Add("CONTROL FIELDS " + plan.ControlFields.Length);
            foreach (FieldInfo field in plan.ControlFields)
                lines.Add(field.DeclaringType.Name + "." + field.Name + " : " + field.FieldType.FullName);
            lines.Add("READ DEPENDENCIES " + plan.ReadCalls);
            foreach (MethodBase method in plan.ReadDependencies)
                lines.Add(method.DeclaringType.FullName + "." + method.Name);
            FieldInfo[] portable = MobBridgeFieldCodec.Fields(plan);
            lines.Add("PORTABLE FIELDS " + portable.Length);
            lines.Add("SCHEMA HASH " + MobBridgeFieldCodec.Hash(plan));
            if (portable.Length < plan.AiFields.Length + plan.ControlFields.Length)
                throw new Exception("Portable bridge schema dropped audited state.");
            HashSet<string> fields = new HashSet<string>();
            foreach (FieldInfo field in portable) fields.Add(field.DeclaringType.Name + "." + field.Name);
            foreach (string required in new[]
            {
                "Humanoid.m_timeSinceLastAttack",
                "Character.m_flying",
                "Character.m_swimTimer",
                "Character.m_rootMotion",
                "Character.m_lavaTimer",
                "Character.m_aboveOrInLavaTimer",
                "MonsterAI.m_updateTargetTimer",
                "MonsterAI.m_updateWeaponTimer",
                "MonsterAI.m_timeSinceSensedTargetCreature",
                "MonsterAI.m_timeSinceAttacking"
            })
                if (!fields.Contains(required)) throw new Exception("Missing combat decision input: " + required);
            HashSet<string> reads = new HashSet<string>();
            foreach (MethodBase method in plan.ReadDependencies)
                reads.Add(method.DeclaringType.FullName + "." + method.Name);
            HashSet<string> copies = new HashSet<string>();
            foreach (MethodInfo method in plan.CopiedMethods)
                copies.Add(method.DeclaringType.FullName + "." + method.Name);
            if (reads.Contains("UnityEngine.Time.get_time"))
                throw new Exception("Native fake AI still reads wall Time.time.");
            if (!copies.Contains("BaseAI.CanUseAttack"))
                throw new Exception("Native attack eligibility is not in the copied decision graph.");
            if (!copies.Contains("MonsterAI.SelectBestAttack") || !copies.Contains("Humanoid.EquipBestWeapon"))
                throw new Exception("Native weapon selection is not in the copied decision graph.");
            if (reads.Contains("Character.IsFlying") || copies.Contains("Character.IsFlying") ||
                reads.Contains("Character.IsSwimming") || copies.Contains("Character.IsSwimming"))
                throw new Exception("Native fake AI still reads live flight/swimming state instead of its shadow body.");
            if (!reads.Contains("UnityEngine.Random.Range") || !reads.Contains("UnityEngine.Random.get_value"))
                throw new Exception("Native Unity random calls must remain on the shared RNG state.");
            foreach (string required in new[]
            {
                "Character.InLava", "Character.AboveOrInLava",
                "Character.GetTimeSinceLastAttack", "Attack.IsDone",
                "Character.GetStealthFactor", "Character.GetNoiseRange",
                "UnityEngine.Physics.Raycast", "UnityEngine.Physics.SphereCast",
                "Pathfinding.GetPath"
            })
                if (!reads.Contains(required)) throw new Exception("Missing audited AI input: " + required);
            File.WriteAllLines(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bridge-field-schema.txt"), lines.ToArray());
            return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bridge-field-schema-error.txt"), error.ToString());
            return 1;
        }
    }
}
