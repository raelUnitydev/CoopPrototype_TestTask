using Game.Domain.Items;
using Game.Domain.Session;
using NUnit.Framework;

namespace Game.Tests
{
    public sealed class ApprovalPolicyTests
    {
        private static readonly ApprovalPolicy Policy = new ApprovalPolicy(maxPlayers: 4, buildVersion: "1.0");

        private static ConnectionRequest Request(string version = "1.0", string id = "abc") =>
            new ConnectionRequest(version, new PlayerId(id), "Tester");

        [Test]
        public void Accepts_ValidRequest()
        {
            ApprovalResult result = Policy.Evaluate(Request(), new ApprovalContext(1, false, SessionPhase.Lobby));
            Assert.IsTrue(result.Approved);
        }

        [Test]
        public void Rejects_WhenFull()
        {
            ApprovalResult result = Policy.Evaluate(Request(), new ApprovalContext(4, false, SessionPhase.Lobby));
            Assert.AreEqual(DisconnectReason.ServerFull, result.Reason);
        }

        [Test]
        public void Rejects_OtherVersion_BeforeAnythingElse()
        {
            ApprovalResult result = Policy.Evaluate(Request("2.0"), new ApprovalContext(4, true, SessionPhase.Lobby));
            Assert.AreEqual(DisconnectReason.VersionMismatch, result.Reason);
        }

        [Test]
        public void Rejects_DuplicatePlayer()
        {
            ApprovalResult result = Policy.Evaluate(Request(), new ApprovalContext(1, true, SessionPhase.InGame));
            Assert.AreEqual(DisconnectReason.DuplicatePlayer, result.Reason);
        }

        [Test]
        public void Rejects_InvalidPlayerId()
        {
            ApprovalResult result = Policy.Evaluate(Request(id: " "), new ApprovalContext(0, false, SessionPhase.None));
            Assert.AreEqual(DisconnectReason.InvalidPayload, result.Reason);
        }

        [Test]
        public void Rejects_WhileLoading_AcceptsLateJoinInGame()
        {
            Assert.AreEqual(DisconnectReason.SessionBusy,
                Policy.Evaluate(Request(), new ApprovalContext(1, false, SessionPhase.Loading)).Reason);
            Assert.IsTrue(Policy.Evaluate(Request(), new ApprovalContext(1, false, SessionPhase.InGame)).Approved);
        }
    }

    public sealed class ItemRulesTests
    {
        private static readonly ItemRulesConfig Config = ItemRulesConfig.Default;

        [Test]
        public void SecondGrab_OfSameItem_IsRejected()
        {
            Assert.AreEqual(ItemRequestResult.Ok, ItemRules.CanGrab(ItemRules.NoHolder, false, 1f, Config));
            Assert.AreEqual(ItemRequestResult.AlreadyHeld, ItemRules.CanGrab(1, false, 1f, Config));
        }

        [Test]
        public void Grab_TooFar_IsRejected()
        {
            float distance = Config.MaxGrabDistance + Config.LatencyTolerance + 0.1f;
            Assert.AreEqual(ItemRequestResult.TooFar, ItemRules.CanGrab(ItemRules.NoHolder, false, distance, Config));
        }

        [Test]
        public void Release_ByNonHolder_IsRejected()
        {
            Assert.AreEqual(ItemRequestResult.NotHolder, ItemRules.CanRelease(2, 1, 0.5f, Config));
            Assert.AreEqual(ItemRequestResult.Ok, ItemRules.CanRelease(1, 1, 0.5f, Config));
        }

        [Test]
        public void ThrowSpeed_IsClamped()
        {
            float scale = ItemRules.ThrowSpeedScale(Config.MaxThrowSpeed * 2f, Config);
            Assert.AreEqual(0.5f, scale, 1e-4f);
            Assert.AreEqual(1f, ItemRules.ThrowSpeedScale(1f, Config));
        }

        [Test]
        public void Spawn_RespectsCooldownAndLimit()
        {
            Assert.AreEqual(ItemRequestResult.RateLimited, ItemRules.CanSpawn(0, 10.1, 10.0, Config));
            Assert.AreEqual(ItemRequestResult.Ok, ItemRules.CanSpawn(0, 11.0, 10.0, Config));
            Assert.AreEqual(ItemRequestResult.LimitReached, ItemRules.CanSpawn(Config.MaxItems, 100, 0, Config));
        }
    }
}
