﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace OpenSim.Region.Framework.Interfaces
{
    public interface ICombatPresence
    {
        string Team { get; set; }
        void KillAvatar(uint killerObjectLocalID);
        void LeaveCombat();
        void JoinCombat();
    }
}