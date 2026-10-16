using System;
using TooManyLags;

internal static partial class IntegrationTests
{
    private static void TextEditingGrid()
    {
        foreach (Type type in new[] { typeof(Sign), typeof(TeleportWorld) })
        {
            Kinds kind = type == typeof(Sign) ? Kinds.Signs : Kinds.Portals;
            Fresh(kind, defaultInteractives:true, nativeSolo:true, checkIntervalSeconds:.05f);
            var first = Peer(1); var second = Peer(2);
            var text = Object(type, 1, 1, 99); var neighbour = Object(type, 2, 1, 99);
            Tick(0); Tick(2.1f);
            try
            {
                Check("text category retains configured management", controller.ManagesText(text));
                controller.RequestNode(text.m_uid, "RPC_Remove".GetStableHashCode()).NotBefore = 100f;
                AuthorityController.TextEditTarget = delegate(ZDO data) { return ReferenceEquals(data, text) ? 1 : 0; };
                Check("explicit text edit reuses indexed object", controller.PrepareTextEdit(text));
                Tick(2.2f);
                Check("text editor receives only its text object despite server overlap", text.GetOwner() == 1 && neighbour.GetOwner() == 99);
                AuthorityController.TextEditLocked = delegate(ZDO data) { return ReferenceEquals(data, text); };
                AuthorityController.TextEditTarget = null; controller.EndTextEdit(text);
                MovePeer(second, 48); Tick(3); Tick(6);
                Check("text protection does not pin another managed object in the same cell", neighbour.GetOwner() == 1);
                MovePeer(second, 8); Tick(6.2f); Tick(8.3f);
                Check("active text pin rejects fresh overlapping grid requests", text.GetOwner() == 1);
                int attempts = controller.ScheduledAttempts, writes = text.Writes;
                for (int i = 0; i < 100; i++) Tick(9 + i * .05f);
                Check("vetoed text requests do not become repeated ownership work", controller.ScheduledAttempts == attempts && text.Writes == writes);
                AuthorityController.TextEditLocked = null; controller.EndTextEdit(text);
                var node = controller.RequestNode(text.m_uid, "RPC_Remove".GetStableHashCode());
                Check("text root retains its native supported interactions", node != null);
                controller.RequestFinished(node); Tick(15);
                Check("receipt maintenance cannot replay a rejected text transfer", text.GetOwner() == 1);
                MovePeer(first, 8); Tick(16); Tick(19);
                Check("new genuine coverage request can transfer text object after release", text.GetOwner() == 99);
            }
            finally { AuthorityController.TextEditTarget = null; AuthorityController.TextEditLocked = null; }
        }
    }
}
