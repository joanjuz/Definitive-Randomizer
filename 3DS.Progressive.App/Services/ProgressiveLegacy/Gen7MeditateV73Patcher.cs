// Ported from pk3DS.WinForms/Misc/Gen7MeditateV73Patcher.cs at f459e40.
// App IO and verified CRO layout adapters differ; original battle instructions are preserved.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ThreeDS.Progressive.Services.ProgressiveLegacy;

/// <summary>
/// Final validated Gen7 custom battle effects.
///
/// Move 96 - Meditate:
/// target major-status cleanse + target Attack +1.
///
/// Move 108 - Smokescreen:
/// priority 0, selectable single target, Protect-like protection for the user,
/// target Accuracy -1, and succeeds only on the user's first active turn.
///
/// Battle.cro bytes are copied exactly from the in-game validated v8.2 state.
/// Protect 182, Detect 197 and Purify 685 remain vanilla.
/// </summary>
internal static class Gen7MeditateV73Patcher
{
    private const int Meditate = 96;
    private const int Smokescreen = 108;

    private const int Lookup1 = 0x0008731C;
    private const int Lookup2 = 0x00087388;
    private const int Cave = 0x000FCB00;

    private const int PurifyGate = 0x000C34A0;
    private const int MeditateRecipient1 = 0x000C34E0;
    private const int MeditateRecipient2 = 0x000C34E8;

    private const int ProtectRegistry = 0x00105EF0;
    private const int DetectRegistry = 0x00105EF8;
    private const int PurifyRegistry = 0x001067B0;

    private const uint Lookup1Stock = 0xE3A04000;
    private const uint Lookup2Stock = 0xE08A0184;
    private const uint Hook1 = 0xEA01D5F7u;
    private const uint Hook2 = 0xEA01D5ECu;

    private static readonly byte[] ProtectId = Hex("B6 00 00 00");
    private static readonly byte[] DetectId = Hex("C5 00 00 00");
    private static readonly byte[] PurifyId = Hex("AD 02 00 00");

    // Meditate v7.3 recipient fixes already validated before Smokescreen work.
    private static readonly byte[] Recipient1 = Hex("05 60 C4 E5");
    private static readonly byte[] Recipient2 = Hex("0C 60 C4 E5");

    // Exact validated v8.2 combined implementation from the validated Battle.cro.
    private static readonly byte[] CombinedPayload = Hex("60 00 55 E3 03 00 00 0A 6C 00 55 E3 01 00 00 0A 00 40 A0 E3 01 2A FE EA 00 40 A0 E3 03 2A FE EA 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 60 00 55 E3 03 00 00 0A 6C 00 55 E3 06 00 00 0A 84 01 8A E0 0C 2A FE EA 0C 00 8D E2 BA 10 FF EB 98 10 8F E2 04 10 80 E5 0A 2A FE EA 0C 00 8D E2 82 00 00 EB 07 2A FE EA F0 41 2D E9 00 40 A0 E1 19 80 D4 E5 00 50 A0 E3 05 10 A0 E1 02 00 A0 E3 CE 92 FD EB 00 60 B0 E1 0A 00 00 0A B8 03 D6 E1 03 00 50 E3 03 00 00 1A 30 00 96 E5 D0 01 E4 E7 08 00 50 E1 09 00 00 0A 06 00 A0 E1 79 92 FD EB 00 60 B0 E1 F4 FF FF 1A 01 50 85 E2 18 00 55 E3 EC FF FF 3A 04 00 A0 E1 F0 41 BD E8 89 96 FF EA 3B 02 D4 E5 68 11 94 E5 07 00 11 E3 03 00 80 12 03 00 50 E3 03 00 A0 83 F0 81 BD E8 00 00 00 00 0F 40 2D E9 04 D0 4D E2 0D 00 A0 E1 8E 10 FF EB 14 10 9F E5 01 10 8F E0 04 10 80 E5 04 D0 8D E2 0F 40 BD E8 05 00 00 EA 00 F0 20 E3 DC 67 FC FF 00 F0 20 E3 00 F0 20 E3 00 F0 20 E3 00 F0 20 E3 F0 5F 2D E9 03 00 A0 E3 01 A0 A0 E1 02 90 A0 E1 C0 2B FE EB 09 00 50 E1 41 00 00 1A 00 80 A0 E3 05 00 A0 E3 BB 2B FE EB 00 70 50 E2 00 40 A0 E3 01 B0 A0 83 3A 00 00 9A 06 00 84 E2 FF 00 00 E2 B4 2B FE EB FF 60 00 E2 06 10 A0 E1 0A 00 A0 E1 80 55 FE EB 00 F0 20 E3 00 F0 20 E3 EE 14 FE EB 00 50 B0 E1 00 F0 20 E3 0A 00 00 0A 09 20 A0 E1 0A 10 A0 E3 0A 00 A0 E1 8E 2A FE EB 04 50 C0 E5 11 B0 C0 E5 00 10 A0 E1 05 60 C0 E5 0A 00 A0 E1 07 00 FE EB 01 80 A0 E3 01 40 84 E2 04 00 57 E1 E4 FF FF 8A 00 00 58 E3 00 F0 20 E3 09 20 A0 E1 0D 10 A0 E3 0A 00 A0 E1 7E 2A FE EB 12 10 D0 E5 00 40 A0 E1 01 20 A0 E3 01 00 A0 E3 04 20 C4 E5 11 00 C4 E5 26 00 81 E3 10 20 C4 E5 03 30 A0 E3 12 00 C4 E5 03 00 A0 E1 05 60 C4 E5 89 2B FE EB 0C 60 C4 E5 04 10 A0 E1 0A 00 A0 E1 F0 5F BD E8 EB FF FD EA 00 F0 20 E3 00 F0 20 E3 00 F0 20 E3 00 F0 20 E3 00 F0 20 E3 00 F0 20 E3 00 F0 20 E3 00 F0 20 E3 F0 9F BD E8 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 07 10 A0 E3 00 10 80 E5 10 00 8F E2 1E FF 2F E1 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 03 00 00 00 B0 A1 7A 00 25 00 00 00 BC 4E 7A 00 26 00 00 00 54 95 7A 00 BF 00 00 00 E0 9D 7D 00 5C 00 00 00 A8 6B 7A 00 45 00 00 00 78 EC 7A 00 8E 00 00 00 0C 54 7A 00 00 00 00 00 00 00 00 00 70 47 2D E9 01 A0 A0 E1 02 90 A0 E1 03 80 A0 E1 03 00 A0 E3 57 2B FE EB 09 00 50 E1 20 00 00 1A 05 00 A0 E3 53 2B FE EB 00 00 50 E3 1C 00 00 0A 06 00 A0 E3 4F 2B FE EB FF 60 00 E2 0A 10 A0 E1 09 20 A0 E1 08 30 A0 E1 D6 16 FF EB 09 20 A0 E1 0D 10 A0 E3 0A 00 A0 E1 2E 2A FE EB 12 10 D0 E5 00 40 A0 E1 06 20 A0 E3 FF 00 A0 E3 04 20 C4 E5 11 00 C4 E5 26 00 81 E3 01 30 A0 E3 10 30 C4 E5 12 00 C4 E5 03 00 A0 E3 05 60 C4 E5 39 2B FE EB 0C 60 C4 E5 04 10 A0 E1 0A 00 A0 E1 70 47 BD E8 9B FF FD EA 70 87 BD E8 30 40 2D E9 00 40 A0 E1 01 50 A0 E1 03 00 58 E3 19 00 00 1A 07 10 A0 E1 02 00 A0 E3 09 92 FD EB 00 00 50 E3 0F 00 00 0A B8 13 D0 E1 03 00 51 E3 01 00 00 0A B9 91 FD EB F8 FF FF EA 30 10 90 E5 A1 11 A0 E1 1F 10 01 E2 06 00 A0 E1 EF 54 FE EB 00 00 50 E3 03 00 00 0A 09 10 A0 E3 13 4C FE EB 00 00 50 E3 08 00 00 1A 14 00 94 E5 00 00 50 E3 01 00 00 0A 04 00 A0 E1 5D 41 FE EB 05 10 A0 E1 04 00 A0 E1 33 43 FE EB 30 80 BD E8 00 00 A0 E3 30 80 BD E8 00 00 00 00 00 00 00 00 00 00 00 00 B0 40 2D E9 01 40 A0 E1 02 50 A0 E1 8E 0E FF EB 00 70 A0 E1 03 00 54 E3 12 00 00 1A 00 00 57 E3 10 00 00 0A 04 20 95 E5 06 10 A0 E3 07 00 A0 E1 5F 91 FD EB 00 20 95 E5 05 10 A0 E3 07 00 A0 E1 5B 91 FD EB 00 10 95 E5 A1 11 A0 E1 1F 10 01 E2 06 00 A0 E1 C5 54 FE EB 00 00 50 E3 01 00 00 0A 09 10 A0 E3 6C 46 FE EB 07 00 A0 E1 B0 80 BD E8 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00");
    // Canonical Meditate + Smokescreen v8.2 payload before Mist integration.
    // Allows an already-v8.2 Battle.cro to upgrade in place.
    private static readonly byte[] PreviousCombinedPayloadBeforeMist =
        Hex("60 00 55 E3 03 00 00 0A 6C 00 55 E3 01 00 00 0A 00 40 A0 E3 01 2A FE EA 00 40 A0 E3 03 2A FE EA 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 60 00 55 E3 03 00 00 0A 6C 00 55 E3 06 00 00 0A 84 01 8A E0 0C 2A FE EA 0C 00 8D E2 BA 10 FF EB 98 10 8F E2 04 10 80 E5 0A 2A FE EA 0C 00 8D E2 82 00 00 EB 07 2A FE EA 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 0F 40 2D E9 04 D0 4D E2 0D 00 A0 E1 8E 10 FF EB 14 10 9F E5 01 10 8F E0 04 10 80 E5 04 D0 8D E2 0F 40 BD E8 05 00 00 EA 00 F0 20 E3 DC 67 FC FF 00 F0 20 E3 00 F0 20 E3 00 F0 20 E3 00 F0 20 E3 F0 5F 2D E9 03 00 A0 E3 01 A0 A0 E1 02 90 A0 E1 C0 2B FE EB 09 00 50 E1 41 00 00 1A 00 80 A0 E3 05 00 A0 E3 BB 2B FE EB 00 70 50 E2 00 40 A0 E3 01 B0 A0 83 3A 00 00 9A 06 00 84 E2 FF 00 00 E2 B4 2B FE EB FF 60 00 E2 06 10 A0 E1 0A 00 A0 E1 80 55 FE EB 00 F0 20 E3 00 F0 20 E3 EE 14 FE EB 00 50 B0 E1 00 F0 20 E3 0A 00 00 0A 09 20 A0 E1 0A 10 A0 E3 0A 00 A0 E1 8E 2A FE EB 04 50 C0 E5 11 B0 C0 E5 00 10 A0 E1 05 60 C0 E5 0A 00 A0 E1 07 00 FE EB 01 80 A0 E3 01 40 84 E2 04 00 57 E1 E4 FF FF 8A 00 00 58 E3 00 F0 20 E3 09 20 A0 E1 0D 10 A0 E3 0A 00 A0 E1 7E 2A FE EB 12 10 D0 E5 00 40 A0 E1 01 20 A0 E3 01 00 A0 E3 04 20 C4 E5 11 00 C4 E5 26 00 81 E3 10 20 C4 E5 03 30 A0 E3 12 00 C4 E5 03 00 A0 E1 05 60 C4 E5 89 2B FE EB 0C 60 C4 E5 04 10 A0 E1 0A 00 A0 E1 F0 5F BD E8 EB FF FD EA 00 F0 20 E3 00 F0 20 E3 00 F0 20 E3 00 F0 20 E3 00 F0 20 E3 00 F0 20 E3 00 F0 20 E3 00 F0 20 E3 F0 9F BD E8 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 07 10 A0 E3 00 10 80 E5 10 00 8F E2 1E FF 2F E1 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 03 00 00 00 B0 A1 7A 00 25 00 00 00 BC 4E 7A 00 26 00 00 00 54 95 7A 00 BF 00 00 00 E0 9D 7D 00 5C 00 00 00 A8 6B 7A 00 45 00 00 00 78 EC 7A 00 8E 00 00 00 0C 54 7A 00 00 00 00 00 00 00 00 00 70 47 2D E9 01 A0 A0 E1 02 90 A0 E1 03 80 A0 E1 03 00 A0 E3 57 2B FE EB 09 00 50 E1 20 00 00 1A 05 00 A0 E3 53 2B FE EB 00 00 50 E3 1C 00 00 0A 06 00 A0 E3 4F 2B FE EB FF 60 00 E2 0A 10 A0 E1 09 20 A0 E1 08 30 A0 E1 D6 16 FF EB 09 20 A0 E1 0D 10 A0 E3 0A 00 A0 E1 2E 2A FE EB 12 10 D0 E5 00 40 A0 E1 06 20 A0 E3 FF 00 A0 E3 04 20 C4 E5 11 00 C4 E5 26 00 81 E3 01 30 A0 E3 10 30 C4 E5 12 00 C4 E5 03 00 A0 E3 05 60 C4 E5 39 2B FE EB 0C 60 C4 E5 04 10 A0 E1 0A 00 A0 E1 70 47 BD E8 9B FF FD EA 70 87 BD E8 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00");

    // Shared holes used by Gen7MistOwnerActivePatcher.
    private static readonly byte[] MistCritSharedPayload =
        Hex("F0 41 2D E9 00 40 A0 E1 19 80 D4 E5 00 50 A0 E3 05 10 A0 E1 02 00 A0 E3 CE 92 FD EB 00 60 B0 E1 0A 00 00 0A B8 03 D6 E1 03 00 50 E3 03 00 00 1A 30 00 96 E5 D0 01 E4 E7 08 00 50 E1 09 00 00 0A 06 00 A0 E1 79 92 FD EB 00 60 B0 E1 F4 FF FF 1A 01 50 85 E2 18 00 55 E3 EC FF FF 3A 04 00 A0 E1 F0 41 BD E8 89 96 FF EA 3B 02 D4 E5 68 11 94 E5 07 00 11 E3 03 00 80 12 03 00 50 E3 03 00 A0 83 F0 81 BD E8");

    private static readonly byte[] MistGateCommitSharedPayload =
        Hex("30 40 2D E9 00 40 A0 E1 01 50 A0 E1 03 00 58 E3 19 00 00 1A 07 10 A0 E1 02 00 A0 E3 09 92 FD EB 00 00 50 E3 0F 00 00 0A B8 13 D0 E1 03 00 51 E3 01 00 00 0A B9 91 FD EB F8 FF FF EA 30 10 90 E5 A1 11 A0 E1 1F 10 01 E2 06 00 A0 E1 EF 54 FE EB 00 00 50 E3 03 00 00 0A 09 10 A0 E3 13 4C FE EB 00 00 50 E3 08 00 00 1A 14 00 94 E5 00 00 50 E3 01 00 00 0A 04 00 A0 E1 5D 41 FE EB 05 10 A0 E1 04 00 A0 E1 33 43 FE EB 30 80 BD E8 00 00 A0 E3 30 80 BD E8 00 00 00 00 00 00 00 00 00 00 00 00 B0 40 2D E9 01 40 A0 E1 02 50 A0 E1 8E 0E FF EB 00 70 A0 E1 03 00 54 E3 12 00 00 1A 00 00 57 E3 10 00 00 0A 04 20 95 E5 06 10 A0 E3 07 00 A0 E1 5F 91 FD EB 00 20 95 E5 05 10 A0 E3 07 00 A0 E1 5B 91 FD EB 00 10 95 E5 A1 11 A0 E1 1F 10 01 E2 06 00 A0 E1 C5 54 FE EB 00 00 50 E3 01 00 00 0A 09 10 A0 E3 6C 46 FE EB 07 00 A0 E1 B0 80 BD E8");

    // Previous Meditate-only source payload, if the old source exposed it.
    private static readonly byte[] PreviousMeditatePayload = Hex("");
    // Previous permanent combined Smokescreen v7.1e payload.
    // Allows an already-v7.1e Battle.cro to upgrade directly to v8.2.
    private static readonly byte[] PreviousCombinedPayload = Hex("60 00 55 E3 03 00 00 0A 6C 00 55 E3 01 00 00 0A 00 40 A0 E3 01 2A FE EA 00 40 A0 E3 03 2A FE EA 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 60 00 55 E3 03 00 00 0A 6C 00 55 E3 06 00 00 0A 84 01 8A E0 0C 2A FE EA 0C 00 8D E2 BA 10 FF EB 98 10 8F E2 04 10 80 E5 0A 2A FE EA 0C 00 8D E2 82 00 00 EB 07 2A FE EA 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 0F 40 2D E9 04 D0 4D E2 0D 00 A0 E1 8E 10 FF EB 14 10 9F E5 01 10 8F E0 04 10 80 E5 04 D0 8D E2 0F 40 BD E8 05 00 00 EA 00 F0 20 E3 DC 67 FC FF 00 F0 20 E3 00 F0 20 E3 00 F0 20 E3 00 F0 20 E3 F0 5F 2D E9 03 00 A0 E3 01 A0 A0 E1 02 90 A0 E1 C0 2B FE EB 09 00 50 E1 41 00 00 1A 00 80 A0 E3 05 00 A0 E3 BB 2B FE EB 00 70 50 E2 00 40 A0 E3 01 B0 A0 83 3A 00 00 9A 06 00 84 E2 FF 00 00 E2 B4 2B FE EB FF 60 00 E2 06 10 A0 E1 0A 00 A0 E1 80 55 FE EB 00 F0 20 E3 00 F0 20 E3 EE 14 FE EB 00 50 B0 E1 00 F0 20 E3 0A 00 00 0A 09 20 A0 E1 0A 10 A0 E3 0A 00 A0 E1 8E 2A FE EB 04 50 C0 E5 11 B0 C0 E5 00 10 A0 E1 05 60 C0 E5 0A 00 A0 E1 07 00 FE EB 01 80 A0 E3 01 40 84 E2 04 00 57 E1 E4 FF FF 8A 00 00 58 E3 00 F0 20 E3 09 20 A0 E1 0D 10 A0 E3 0A 00 A0 E1 7E 2A FE EB 12 10 D0 E5 00 40 A0 E1 01 20 A0 E3 01 00 A0 E3 04 20 C4 E5 11 00 C4 E5 26 00 81 E3 10 20 C4 E5 03 30 A0 E3 12 00 C4 E5 03 00 A0 E1 05 60 C4 E5 89 2B FE EB 0C 60 C4 E5 04 10 A0 E1 0A 00 A0 E1 F0 5F BD E8 EB FF FD EA 00 F0 20 E3 00 F0 20 E3 00 F0 20 E3 00 F0 20 E3 00 F0 20 E3 00 F0 20 E3 00 F0 20 E3 00 F0 20 E3 F0 9F BD E8 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 10 40 2D E9 F4 0F FF EB 00 40 A0 E1 1C 10 8F E2 0C 10 84 E5 64 10 8F E2 1C 10 84 E5 04 00 A0 E1 10 80 BD E8 00 00 00 00 00 00 00 00 00 00 00 00 1F 40 2D E9 04 D0 4D E2 0D 00 A0 E1 E6 0F FF EB 00 40 A0 E1 10 10 9F E5 01 10 8F E0 0C 10 84 E5 04 D0 8D E2 1F 40 BD E8 37 2C FF EA 38 03 FD FF 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 0F 40 2D E9 04 D0 4D E2 0D 00 A0 E1 D2 0F FF EB 18 10 9F E5 01 10 8F E0 1C 10 80 E5 04 D0 8D E2 0F 00 9D E8 D7 16 FF EB 0F 40 BD E8 13 00 00 EA 6C 5B FC FF 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 F0 5F 2D E9 03 00 A0 E3 01 A0 A0 E1 02 90 A0 E1 30 2B FE EB 09 00 50 E1 41 00 00 1A 00 80 A0 E3 05 00 A0 E3 2B 2B FE EB 00 70 50 E2 00 40 A0 E3 01 B0 A0 83 3A 00 00 9A 06 00 84 E2 FF 00 00 E2 24 2B FE EB FF 60 00 E2 06 10 A0 E1 0A 00 A0 E1 F0 54 FE EB 00 F0 20 E3 00 F0 20 E3 5E 14 FE EB 00 50 B0 E1 00 F0 20 E3 0A 00 00 0A 09 20 A0 E1 0A 10 A0 E3 0A 00 A0 E1 FE 29 FE EB 04 50 C0 E5 11 B0 C0 E5 00 10 A0 E1 05 60 C0 E5 0A 00 A0 E1 00 F0 20 E3 01 80 A0 E3 01 40 84 E2 04 00 57 E1 E4 FF FF 8A 00 00 58 E3 00 F0 20 E3 09 20 A0 E1 0D 10 A0 E3 0A 00 A0 E1 EE 29 FE EB 12 10 D0 E5 00 40 A0 E1 06 20 A0 E3 FF 00 A0 E3 04 20 C4 E5 11 00 C4 E5 26 00 81 E3 01 30 A0 E3 10 30 C4 E5 12 00 C4 E5 03 00 A0 E3 05 60 C4 E5 F9 2A FE EB 0C 60 C4 E5 04 10 A0 E1 0A 00 A0 E1 F0 5F BD E8 5B FF FD EA 00 F0 20 E3 00 F0 20 E3 00 F0 20 E3 00 F0 20 E3 00 F0 20 E3 00 F0 20 E3 00 F0 20 E3 00 F0 20 E3 F0 9F BD E8");

    internal static bool IsRequested(CustomBattleEffectPatcher.BattlePatchRequest request)
    {
        if (request is null)
            return false;

        return request.Move switch
        {
            Meditate => HasMeditateToken(request.BattlePatch),
            Smokescreen => HasSmokescreenToken(request.BattlePatch),
            _ => false,
        };
    }

    internal static void ConfigureMoveData(int generation, int move, byte[] data, string battlePatch)
    {
        if (generation != 7 || data is null || data.Length < 0x1E)
            return;

        if (move == Meditate && HasMeditateToken(battlePatch))
        {
            ConfigureMeditate(data);
            return;
        }

        if (move == Smokescreen && HasSmokescreenToken(battlePatch))
            ConfigureSmokescreen(data);
    }

    internal static int Apply()
    {
        string path = FindBattleCro();

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return -1;

        byte[] data = File.ReadAllBytes(path);

        int ProtectRegistry=LegacyCroLayout.StockOffset(data,Gen7MeditateV73Patcher.ProtectRegistry);
        int DetectRegistry=LegacyCroLayout.StockOffset(data,Gen7MeditateV73Patcher.DetectRegistry);
        int PurifyRegistry=LegacyCroLayout.StockOffset(data,Gen7MeditateV73Patcher.PurifyRegistry);

        if (data.Length < Cave + CombinedPayload.Length ||
            data.Length < PurifyRegistry + 8)
        {
            return -1;
        }

        // Never hijack these vanilla registry entries.
        if (!Match(data, ProtectRegistry, ProtectId) ||
            !Match(data, DetectRegistry, DetectId) ||
            !Match(data, PurifyRegistry, PurifyId))
        {
            return -1;
        }

        bool alreadyFinal =
            ReadUInt32(data, Lookup1) == Hook1 &&
            ReadUInt32(data, Lookup2) == Hook2 &&
            Match(data, Cave, CombinedPayload) &&
            Match(data, MeditateRecipient1, Recipient1) &&
            Match(data, MeditateRecipient2, Recipient2);

        if (alreadyFinal)
            return 0;

        bool cleanBase =
            ReadUInt32(data, Lookup1) == Lookup1Stock &&
            ReadUInt32(data, Lookup2) == Lookup2Stock &&
            IsZero(data, Cave, CombinedPayload.Length);

        bool previousMeditate =
            PreviousMeditatePayload.Length != 0 &&
            PreviousMeditatePayload.Length <= CombinedPayload.Length &&
            ReadUInt32(data, Lookup1) == Hook1 &&
            ReadUInt32(data, Lookup2) == Hook2 &&
            Match(data, Cave, PreviousMeditatePayload) &&
            IsZero(
                data,
                Cave + PreviousMeditatePayload.Length,
                CombinedPayload.Length - PreviousMeditatePayload.Length);

        bool previousCombined =
            PreviousCombinedPayload.Length != 0 &&
            PreviousCombinedPayload.Length == CombinedPayload.Length &&
            ReadUInt32(data, Lookup1) == Hook1 &&
            ReadUInt32(data, Lookup2) == Hook2 &&
            Match(data, Cave, PreviousCombinedPayload);

        bool previousBeforeMist =
            PreviousCombinedPayloadBeforeMist.Length == CombinedPayload.Length &&
            ReadUInt32(data, Lookup1) == Hook1 &&
            ReadUInt32(data, Lookup2) == Hook2 &&
            Match(data, Cave, PreviousCombinedPayloadBeforeMist);

        // Allows Mist to be applied by itself on a clean Battle.cro.  If a
        // later template requests Meditate/Smokescreen, this state upgrades
        // safely to the full merged CombinedPayload.
        bool mistOnlyBase =
            ReadUInt32(data, Lookup1) == Lookup1Stock &&
            ReadUInt32(data, Lookup2) == Lookup2Stock &&
            IsZero(data, Cave, 0x78) &&
            Match(data, Cave + 0x78, MistCritSharedPayload) &&
            IsZero(data, Cave + 0xFC, 0x28C) &&
            Match(data, Cave + 0x388, MistGateCommitSharedPayload) &&
            IsZero(data, Cave + 0x498, 0x10);

        if (!cleanBase &&
            !previousMeditate &&
            !previousCombined &&
            !previousBeforeMist &&
            !mistOnlyBase)
        {
            return -1;
        }

        BackupOnce(path, ".bak_meditate_smokescreen_v82");

        int changed = 0;

        changed += WriteBytesIfDifferent(data, Cave, CombinedPayload) ? 1 : 0;
        changed += WriteUInt32IfDifferent(data, Lookup1, Hook1) ? 1 : 0;
        changed += WriteUInt32IfDifferent(data, Lookup2, Hook2) ? 1 : 0;

        changed += WriteBytesIfDifferent(data, MeditateRecipient1, Recipient1) ? 1 : 0;
        changed += WriteBytesIfDifferent(data, MeditateRecipient2, Recipient2) ? 1 : 0;

        File.WriteAllBytes(path, data);
        return changed;
    }

    internal static void EnsureTemplateRow(string path)
    {
        EnsureMoveRow(path, Meditate, ConfigureMeditateTemplate);
        EnsureMoveRow(path, Smokescreen, ConfigureSmokescreenTemplate);
    }

    private static void ConfigureMeditate(byte[] data)
    {
        data[0x01] = 13; // Unique Effect
        data[0x02] = 0;  // Status

        data[0x08] = 0;
        data[0x09] = 0;
        data[0x0A] = 0;

        data[0x10] = 0x8F; // 399
        data[0x11] = 0x01;

        data[0x13] = 0;
        data[0x14] = 0; // Single

        for (int i = 0x15; i <= 0x1D; i++)
            data[i] = 0;
    }

    private static void ConfigureSmokescreen(byte[] data)
    {
        data[0x01] = 13; // Unique Effect
        data[0x02] = 0;  // Status

        data[0x08] = 0;
        data[0x09] = 0;
        data[0x0A] = 0;

        data[0x10] = 111; // Protect special route
        data[0x11] = 0;

        data[0x13] = 0;
        data[0x14] = 0; // Single

        // Accuracy -1 is emitted by Battle.cro.
        for (int i = 0x15; i <= 0x1D; i++)
            data[i] = 0;
    }

    private static void ConfigureMeditateTemplate(string[] fields)
    {
        fields[2] = "Status";
        fields[3] = "13";
        fields[12] = "399";

        fields[20] = "Single";
        fields[21] = "true";

        for (int i = 22; i <= 36; i++)
            fields[i] = string.Empty;

        fields[41] = "Gen7MeditateV73";
        fields[42] = "Meditacion / Meditate: target status cleanse +1 Attack (v7.3)";
    }

    private static void ConfigureSmokescreenTemplate(string[] fields)
    {
        fields[2] = "Status";
        fields[3] = "13";
        fields[7] = "0";
        fields[12] = "111";

        fields[20] = "Single";
        fields[21] = "true";

        for (int i = 22; i <= 36; i++)
            fields[i] = string.Empty;

        fields[41] = "Gen7SmokescreenV82";
        fields[42] = "Pantalla de Humo / Smokescreen: first-active-turn self-protection + selected target Accuracy -1; doubles-safe private descriptors (v8.2)";
    }

    private static bool HasMeditateToken(string value)
        => HasAnyToken(value,
            "Gen7MeditateV73",
            "Gen7MeditateCleanseAttack",
            "MeditateCleanseAttack",
            "MeditacionCuraAtaque");

    private static bool HasSmokescreenToken(string value)
        => HasAnyToken(value,
            "Gen7SmokescreenV82",
            "Gen7SmokescreenV71",
            "Gen7SmokescreenProtectFirstTurn",
            "SmokescreenProtectFirstTurn",
            "PantallaHumoProteccion");

    private static bool HasAnyToken(string value, params string[] wanted)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var set = wanted
            .Select(NormalizeToken)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return value
            .Split([',', ';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeToken)
            .Any(set.Contains);
    }

    private static string NormalizeToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var sb = new StringBuilder(value.Length);

        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
        }

        return sb.ToString();
    }

    private static void EnsureMoveRow(string path, int move, Action<string[]> configure)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        string[] lines = File.ReadAllLines(path);

        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].Trim();

            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                continue;

            string[] fields = SplitCsvLine(lines[i]);

            if (fields.Length == 0 ||
                !int.TryParse(
                    Get(fields,0),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int rowMove) ||
                rowMove != move)
            {
                continue;
            }

            if (fields.Length < 43)
                Array.Resize(ref fields,43);

            configure(fields);
            lines[i] = JoinCsvLine(fields);
            File.WriteAllLines(path,lines);
            return;
        }

        string[] row = new string[43];
        row[0] = move.ToString(CultureInfo.InvariantCulture);
        configure(row);

        string existing = File.ReadAllText(path);
        string separator =
            existing.Length == 0 || existing.EndsWith('\n')
                ? string.Empty
                : Environment.NewLine;

        File.AppendAllText(
            path,
            separator + JoinCsvLine(row) + Environment.NewLine);
    }

    private static string FindBattleCro()
    {
        var roots = new List<string>();

        if (!string.IsNullOrWhiteSpace(Main.RomFSPath))
            roots.Add(Main.RomFSPath);

        roots.Add(Main.Workspace);
        roots.Add(Main.Workspace);

        foreach (string root in roots
                     .Where(Directory.Exists)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string[] candidates =
            [
                Path.Combine(root,"Battle.cro"),
                Path.Combine(root,"ExtractedRomFS","Battle.cro"),
                Path.Combine(root,"RomFS","Battle.cro"),
                Path.Combine(root,"romfs","Battle.cro"),
            ];

            foreach (string candidate in candidates)
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            try
            {
                string match = Directory
                    .GetFiles(root,"Battle.cro",SearchOption.AllDirectories)
                    .OrderByDescending(z => z.Contains("ExtractedRomFS",StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(match))
                    return match;
            }
            catch
            {
            }
        }

        return string.Empty;
    }

    private static void BackupOnce(string path,string suffix)
    {
        if (!AutomaticBackups.Enabled)
            return;

        string backup = path+suffix;

        if (!File.Exists(backup))
            File.Copy(path,backup,false);
    }

    private static uint ReadUInt32(byte[] data,int offset)
    {
        if (offset < 0 || offset+4 > data.Length)
            return uint.MaxValue;

        return BitConverter.ToUInt32(data,offset);
    }

    private static bool WriteUInt32IfDifferent(byte[] data,int offset,uint value)
    {
        if (ReadUInt32(data,offset) == value)
            return false;

        BitConverter.GetBytes(value).CopyTo(data,offset);
        return true;
    }

    private static bool WriteBytesIfDifferent(byte[] data,int offset,byte[] value)
    {
        if (Match(data,offset,value))
            return false;

        value.CopyTo(data,offset);
        return true;
    }

    private static bool Match(byte[] data,int offset,byte[] value)
    {
        if (offset < 0 || offset+value.Length > data.Length)
            return false;

        for (int i=0; i<value.Length; i++)
        {
            if (data[offset+i] != value[i])
                return false;
        }

        return true;
    }

    private static bool IsZero(byte[] data,int offset,int length)
    {
        if (length < 0 || offset < 0 || offset+length > data.Length)
            return false;

        for (int i=0; i<length; i++)
        {
            if (data[offset+i] != 0)
                return false;
        }

        return true;
    }

    private static byte[] Hex(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        string[] parts = value.Split(' ',StringSplitOptions.RemoveEmptyEntries);
        byte[] result = new byte[parts.Length];

        for (int i=0; i<parts.Length; i++)
            result[i] = Convert.ToByte(parts[i],16);

        return result;
    }

    private static string[] SplitCsvLine(string line)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        bool quoted = false;

        for (int i=0; i<line.Length; i++)
        {
            char c = line[i];

            if (c == '"')
            {
                if (quoted && i+1 < line.Length && line[i+1] == '"')
                {
                    sb.Append('"');
                    i++;
                }
                else
                {
                    quoted = !quoted;
                }
            }
            else if (c == ',' && !quoted)
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }

        result.Add(sb.ToString());
        return result.ToArray();
    }

    private static string Get(string[] fields,int index)
        => index >= 0 && index < fields.Length
            ? fields[index]?.Trim() ?? string.Empty
            : string.Empty;

    private static string JoinCsvLine(IEnumerable<string> fields)
        => string.Join(",",fields.Select(EscapeCsvField));

    private static string EscapeCsvField(string value)
    {
        value ??= string.Empty;

        if (value.IndexOfAny([',','"','\r','\n']) < 0)
            return value;

        return "\"" + value.Replace("\"","\"\"") + "\"";
    }
}
