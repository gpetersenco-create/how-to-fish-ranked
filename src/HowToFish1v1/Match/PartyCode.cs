using System;
using Steamworks;

namespace HowToFish1v1.Match
{
    /// <summary>
    /// A short code for the current Steam lobby so friends can join by typing it instead of waiting for an invite.
    /// The code is the lobby id in base 32 (no ambiguous letters); joining hands the id to the game's own lobby join.
    /// </summary>
    public static class PartyCode
    {
        private const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZ";

        public static string Current
        {
            get
            {
                try
                {
                    var id = SteamManager.CurrentLobbyID;
                    if (id == CSteamID.Nil || !ConnectionManager.IsUsingSteam) return "";
                    return Encode(id.m_SteamID);
                }
                catch (Exception) { return ""; }
            }
        }

        public static string Encode(ulong value)
        {
            if (value == 0) return "";
            var chars = new System.Text.StringBuilder();
            while (value > 0) { chars.Insert(0, Alphabet[(int)(value % 32)]); value /= 32; }
            return chars.ToString();
        }

        public static bool TryDecode(string code, out ulong value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(code)) return false;
            foreach (char raw in code.Trim().ToUpperInvariant())
            {
                char c = raw == 'O' ? '0' : raw == 'I' || raw == 'L' ? '1' : raw;
                int d = Alphabet.IndexOf(c);
                if (d < 0) return false;
                value = value * 32 + (ulong)d;
            }
            return value > 0;
        }

        /// <summary>Joins the lobby behind a code through the game's own Steam join path.</summary>
        public static bool Join(string code, out string why)
        {
            why = "";
            if (!TryDecode(code, out ulong id)) { why = "That is not a party code"; return false; }
            try
            {
                try { if (!SteamAPI.IsSteamRunning()) { why = "Steam is not running"; return false; } } catch (Exception) { }
                ModState.RankedSession = true;
                SteamManager.JoinLobby(id);
                Plugin.Log.LogInfo($"Joining lobby {id} from party code {code}");
                return true;
            }
            catch (Exception e)
            {
                why = "Could not join: " + e.Message;
                return false;
            }
        }
    }
}
