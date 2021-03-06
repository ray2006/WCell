using System;
using WCell.Constants;
using WCell.Constants.GameObjects;
using WCell.Constants.Spells;
using WCell.Constants.Updates;
using WCell.Core;
using WCell.RealmServer.Global;
using WCell.RealmServer.Spells;
using WCell.Util.Threading;
using WCell.Core.Timers;
using WCell.RealmServer.Content;
using WCell.RealmServer.Entities;
using WCell.RealmServer.GameObjects.Handlers;
using WCell.RealmServer.Handlers;
using WCell.RealmServer.Battlegrounds;

namespace WCell.RealmServer.Misc
{
	/// <summary>
	/// Represents the progress of a duel between 2 Characters.
	/// Most methods require the context of the Flag's region
	/// </summary>
	public class Duel : IDisposable, IUpdatable
	{
		#region Static Fields and Methods
		private static float s_duelRadius, s_duelRadiusSq;

		/// <summary>
		/// If duelists are further away than the DuelRadius (in yards), the cancel-timer will be started.
		/// </summary>
		public static float DuelRadius
		{
			get
			{
				return s_duelRadius;
			}
			set
			{
				s_duelRadius = value;
				s_duelRadiusSq = value * value;
			}
		}

		public static float DuelRadiusSquare
		{
			get { return s_duelRadiusSq; }
		}

		/// <summary>
		/// The delay until a duel starts in seconds
		/// </summary>
		public static float DefaultStartDelay = 3.0f;

		/// <summary>
		/// If Duelist leaves the area around the flag for longer than this delay, he/she will loose the duel
		/// </summary>
		public static float DefaultCancelDelay = 10.0f;

		static Duel()
		{
			DuelRadius = 25f;
		}

		/// <summary>
		/// Checks several requirements for a new Duel to start.
		/// </summary>
		/// <param name="challenger"></param>
		/// <param name="rival"></param>
		/// <returns></returns>
		public static SpellFailedReason CheckRequirements(Character challenger, Character rival)
		{
			if (challenger.IsDueling)
			{
				return SpellFailedReason.CantDuelWhileStealthed;
			}
			if (challenger.Zone != null && challenger.Zone.Info.IsSanctuary)
			{
				return SpellFailedReason.NotHere;
			}
			if (!challenger.KnowsOf(rival))
			{
				// can't duel what you can't see
				return SpellFailedReason.NoValidTargets;
			}
			if (!rival.KnowsOf(challenger))
			{
				// can't duel when the other can't see you
				return SpellFailedReason.CantDuelWhileInvisible;
			}
			if (rival.IsInCombat)
			{
				// can't duel anyone who is currently in combat
				return SpellFailedReason.TargetInCombat;
			}
			if (rival.DuelOpponent != null)
			{
				// can't duel with someone who is already dueling
				return SpellFailedReason.TargetDueling;
			}
			if (challenger.Region is Battleground)
			{
				// can't duel in Battlegrounds
				return SpellFailedReason.NotHere;
			}
			return SpellFailedReason.Ok;
		}

		/// <summary>
		/// Make sure that the 2 parties may actual duel, by calling <see cref="CheckRequirements"/> before.
		/// </summary>
		/// <param name="challenger"></param>
		/// <param name="rival"></param>
		/// <returns></returns>
		public static Duel InitializeDuel(Character challenger, Character rival)
		{
			return new Duel(challenger, rival, DefaultStartDelay, DefaultCancelDelay);
		}
		#endregion

		private Character m_challenger;
		private Character m_rival;
		private float m_startDelay;
		private float m_challengerCountdown;
		private float m_rivalCountdown;
		private float m_cancelDelay;
		private bool m_active;
		private bool m_accepted;
		private bool m_challengerInRange;
		private bool m_rivalInRange;
		private GameObject m_flag;
		private Region m_region;

		/// <summary>
		/// Creates a new duel between the 2 parties.
		/// </summary>
		/// <param name="challenger"></param>
		/// <param name="rival"></param>
		/// <param name="startDelay"></param>
		/// <param name="cancelDelay"></param>
		public Duel(Character challenger, Character rival, float startDelay, float cancelDelay)
		{
			m_challenger = challenger;
			m_rival = rival;
			m_region = challenger.Region;
			m_startDelay = startDelay;
			m_cancelDelay = cancelDelay;

			m_challenger.Duel = this;
			m_challenger.DuelOpponent = rival;

			m_rival.Duel = this;
			m_rival.DuelOpponent = challenger;

			Initialize();
		}

		/// <summary>
		/// The Character who challenged the Rival for a Duel
		/// </summary>
		public Character Challenger
		{
			get { return m_challenger; }
		}

		/// <summary>
		/// The Character who has been challenged for a Duel
		/// </summary>
		public Character Rival
		{
			get { return m_rival; }
		}

		/// <summary>
		/// The Duel Flag
		/// </summary>
		public GameObject Flag
		{
			get
			{
				return m_flag;
			}
		}

		public float CancelDelay
		{
			get { return m_cancelDelay; }
			set { m_cancelDelay = value; }
		}

		/// <summary>
		/// Delay left in seconds until the Duel starts. 
		/// If countdown did not start yet, will be set to the total delay.
		/// If countdown is already over, this value is redundant.
		/// </summary>
		public float StartDelay
		{
			get { return m_startDelay; }
			set { m_startDelay = value; }
		}

		/// <summary>
		/// A duel is active if after the duel engaged, the given startDelay passed
		/// </summary>
		public bool IsActive
		{
			get { return m_active; }
		}

		/// <summary>
		/// whether the Challenger is in range of the duel-flag.
		/// When not in range, the cancel timer starts.
		/// </summary>
		public bool IsChallengerInRange
		{
			get { return m_challengerInRange; }
		}

		/// <summary>
		/// whether the Rival is in range of the duel-flag.
		/// When not in range, the cancel timer starts.
		/// </summary>
		public bool IsRivalInRange
		{
			get { return m_rivalInRange; }
		}

		/// <summary>
		/// Initializes the duel after a new Duel has been proposed
		/// </summary>
		void Initialize()
		{
			m_flag = GameObject.Create(GOEntryId.DuelFlag);
			m_flag.Phase = m_challenger.Phase;
			if (m_flag == null)
			{
				ContentHandler.OnInvalidDBData("Cannot start Duel: DuelFlag-GameObject (ID: {0}) does not exist.", (int)GOEntryId.DuelFlag);
				Cancel();
			}
			else
			{
				((DuelFlagHandler)m_flag.Handler).Duel = this;
				m_flag.CreatedBy = m_challenger.EntityId;
				m_flag.Level = m_challenger.Level;
				m_flag.AnimationProgress = 255;
				m_flag.Position = m_challenger.Position;
				m_flag.Faction = m_challenger.Faction;
				m_flag.ScaleX = m_challenger.ScaleX;
				m_flag.ParentRotation4 = 1;

				m_region.AddMessage(new Message(() =>
				{
					if (m_flag == null) return;
					m_flag.Orientation = m_challenger.Orientation;
					m_flag.Position = ((m_challenger.Position + m_rival.Position) / 2);
					m_region.AddObjectNow(m_flag);
				}));

				m_region.AddMessage(new Message(() => DuelHandler.SendRequest(m_flag, m_challenger, m_rival)));
				m_challenger.SetEntityId(PlayerFields.DUEL_ARBITER, m_flag.EntityId);
				m_rival.SetEntityId(PlayerFields.DUEL_ARBITER, m_flag.EntityId);
			}
		}

		/// <summary>
		/// Starts the countdown (automatically called when the invited rival accepts)
		/// </summary>
		public void Accept(Character acceptingCharacter)
		{
			if (m_challenger == acceptingCharacter) return;

			var delay = (uint)(m_startDelay * 1000);

			DuelHandler.SendCountdown(m_challenger, delay);
			DuelHandler.SendCountdown(m_rival, delay);

			m_region.RegisterUpdatableLater(this);
			m_accepted = true;
		}

		/// <summary>
		/// Starts the Duel (automatically called after countdown)
		/// </summary>
		/// <remarks>Requires region context</remarks>
		public void Start()
		{
			m_active = true;
			m_challengerInRange = true;
			m_rivalInRange = true;

			m_challenger.SetUInt32(PlayerFields.DUEL_TEAM, (uint)DuelTeam.Challenger);
			m_rival.SetUInt32(PlayerFields.DUEL_TEAM, (uint)DuelTeam.Rival);

			m_challenger.FirstAttacker = m_rival;
			m_rival.FirstAttacker = m_challenger;
		}

		/// <summary>
		/// Ends the duel with the given win-condition and the given loser
		/// </summary>
		/// <param name="loser">The opponent that lost the match or null if its a draw</param>
		/// <remarks>Requires region context</remarks>
		public void Finish(DuelWin win, Character loser)
		{
			if (IsActive)
			{
				if (loser != null)
				{
					// user casts stun on himself
					loser.SpellCast.Start(SpellId.NotDisplayedGrovel, false);
					var winner = loser.DuelOpponent;
					DuelHandler.SendWinner(win, winner, loser);
				}

				// remove all debuffs
				m_challenger.FirstAttacker = null;
				m_rival.FirstAttacker = null;
				m_challenger.Auras.RemoveWhere(aura => !aura.IsBeneficial && aura.CasterInfo.CasterId == m_rival.EntityId);
				m_rival.Auras.RemoveWhere(aura => !aura.IsBeneficial && aura.CasterInfo.CasterId == m_challenger.EntityId);
				if (m_rival.ComboTarget == m_challenger)
				{
					m_rival.ResetComboPoints();
				}
				if (m_challenger.ComboTarget == m_rival)
				{
					m_challenger.ResetComboPoints();
				}
			}

			Dispose();
		}

		/// <summary>
		/// Updates the Duel
		/// </summary>
		/// <param name="dt">the time since the last update in seconds</param>
		public void Update(float dt)
		{
			if (m_challenger == null || m_rival == null)
			{
				Dispose();
				return;
			}

			if (!m_challenger.IsInWorld ||
				!m_rival.IsInWorld ||
				!m_flag.IsInWorld)
			{
				Dispose();
				return;
			}

			if (!m_active)
			{
				if (!m_accepted) return;

				// duel is counting down
				m_startDelay -= dt;
				if (m_startDelay <= 0)
				{
					Start();
				}
			}
			else
			{
				// duel is in progress

				// check if challenger in-range status changed
				if (m_challengerInRange != m_challenger.IsInRadiusSq(m_flag, s_duelRadiusSq))
				{
					if (!(m_challengerInRange = !m_challengerInRange))
					{
						// out of range: Start the cancel-timer
						DuelHandler.SendOutOfBounds(m_challenger, (uint)(m_cancelDelay * 1000));
					}
					else
					{
						// back in range: Cancel the cancel-timer
						m_challengerCountdown = 0f;
						DuelHandler.SendInBounds(m_challenger);
					}
				}
				else
				{
					if (!m_challengerInRange)
					{
						// still out of range
						// progress the cancel-timer
						m_challengerCountdown += dt;
						if (m_challengerCountdown >= m_cancelDelay)
						{
							// cancel the duel
							Finish(DuelWin.OutOfRange, m_challenger);
						}
					}
				}

				// check if rival in-range status changed
				if (m_rivalInRange != m_rival.IsInRadiusSq(m_flag, s_duelRadiusSq))
				{
					if (!(m_rivalInRange = !m_rivalInRange))
					{
						// out of range: Start the cancel-timer
						var delay = (uint)(m_cancelDelay * 1000);
						DuelHandler.SendOutOfBounds(m_rival, delay);
					}
					else
					{
						// back in range: Cancel the cancel-timer
						m_rivalCountdown = 0f;
						DuelHandler.SendInBounds(m_rival);
					}
				}
				else
				{
					if (!m_rivalInRange)
					{
						// still out of range
						// progress the cancel-timer
						m_rivalCountdown += dt;
						if (m_rivalCountdown >= m_cancelDelay)
						{
							// cancel the duel
							Finish(DuelWin.OutOfRange, m_rival);
						}
					}
				}
			}
		}

		/// <summary>
		/// Duel is over due to death of one of the opponents
		/// </summary>
		internal void OnDeath(Character duelist)
		{
			// don't die
			duelist.Health = 1;
			duelist.Auras.RemoveWhere(aura => aura.Caster == duelist.DuelOpponent);
			Finish(DuelWin.Knockout, duelist);
		}

		internal void Cleanup()
		{
			DuelHandler.SendComplete(m_challenger, m_rival, m_active);

			m_active = false;
			var region = m_region;
			region.ExecuteInContext(() =>
			{
				region.UnregisterUpdatable(this);

				if (m_challenger != null)
				{
					m_challenger.SetEntityId(PlayerFields.DUEL_ARBITER, EntityId.Zero);
					m_challenger.SetUInt32(PlayerFields.DUEL_TEAM, 0u);
					m_challenger.Duel = null;
					m_challenger.DuelOpponent = null;
				}
				m_challenger = null;

				if (m_rival != null)
				{
					m_rival.SetEntityId(PlayerFields.DUEL_ARBITER, EntityId.Zero);
					m_rival.SetUInt32(PlayerFields.DUEL_TEAM, 0u);
					m_rival.Duel = null;
					m_rival.DuelOpponent = null;
				}
				m_rival = null;
			});
		}

		public void Cancel()
		{
			Dispose();
		}

		/// <summary>
		/// Disposes the duel (called automatically when duel ends)
		/// </summary>
		/// <remarks>Requires region context</remarks>
		public void Dispose()
		{
			if (m_flag != null)
			{
				// flag-handler will call Cleanup() on remove
				m_flag.Delete();
			}
			else
			{
				Cleanup();
			}
		}
	}
}