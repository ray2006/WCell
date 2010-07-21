﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using WCell.Constants;
using WCell.RealmServer.Entities;

namespace WCell.RealmServer.Spells.Auras.Mod
{
	/// <summary>
	/// TODO: Reapply when AP changes
	/// </summary>
	public class ModSpellPowerByAPPctHandler : AuraEffectHandler
	{
		private int[] values;

		protected internal override void Apply()
		{
			var owner = Owner as Character;
			if (owner == null)
			{
				return;
			}

			values = new int[m_spellEffect.MiscBitSet.Length];
			foreach (var school in m_spellEffect.MiscBitSet)
			{
				var sp = owner.GetDamageDoneMod((DamageSchool)school);
				var val = (sp * EffectValue + 50) / 100;
				values[school] = val;
				owner.AddDamageMod((DamageSchool)school, val);
			}
		}

		protected internal override void Remove(bool cancelled)
		{
			var owner = Owner as Character;
			if (owner == null)
			{
				return;
			}

			foreach (var school in m_spellEffect.MiscBitSet)
			{
				owner.RemoveDamageMod((DamageSchool)school, values[school]);
			}
		}
	}
}
