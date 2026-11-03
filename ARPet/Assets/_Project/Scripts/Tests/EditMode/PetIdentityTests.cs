using System.Collections.Generic;
using NUnit.Framework;
using ZQXNXS.ARPet.Core;

namespace ZQXNXS.ARPet.Tests
{
    public sealed class PetIdentityTests
    {
        [Test]
        public void DefaultCatalog_HasFourUniqueStableBindings()
        {
            Assert.IsTrue(PetIdentityCatalog.TryValidate(PetIdentityCatalog.Default, out var error), error);
            Assert.AreEqual(4, PetIdentityCatalog.Default.Count);
            Assert.AreEqual("pet_classic", PetIdentityCatalog.Default[0].PetId);
            Assert.AreEqual("card_divine", PetIdentityCatalog.Default[3].TargetId);
        }

        [Test]
        public void Validation_RejectsDuplicatePetOrTargetIds()
        {
            var duplicatePet = new List<PetIdentity>(PetIdentityCatalog.Default)
            {
                new PetIdentity("pet_classic", "another_target", PetVariantId.Calm),
            };
            Assert.IsFalse(PetIdentityCatalog.TryValidate(duplicatePet, out var petError));
            StringAssert.Contains("PetId", petError);

            var duplicateTarget = new List<PetIdentity>(PetIdentityCatalog.Default)
            {
                new PetIdentity("another_pet", "card_classic", PetVariantId.Calm),
            };
            Assert.IsFalse(PetIdentityCatalog.TryValidate(duplicateTarget, out var targetError));
            StringAssert.Contains("TargetId", targetError);
        }

        [Test]
        public void SortedCopy_IsDeterministicAndDoesNotMutateSource()
        {
            var source = new List<PetIdentity>
            {
                PetIdentityCatalog.Default[2],
                PetIdentityCatalog.Default[0],
                PetIdentityCatalog.Default[3],
            };

            var sorted = PetIdentityCatalog.SortedCopy(source);

            Assert.AreEqual("pet_rage", source[0].PetId);
            Assert.AreEqual("pet_classic", sorted[0].PetId);
            Assert.AreEqual("pet_divine", sorted[1].PetId);
            Assert.AreEqual("pet_rage", sorted[2].PetId);
        }

        [Test]
        public void InvalidCharacters_AreRejectedFromStableIds()
        {
            var invalid = new[]
            {
                new PetIdentity("pet classic", "target_a", PetVariantId.Classic),
                new PetIdentity("pet_a", "target/a", PetVariantId.Classic),
            };

            foreach (var identity in invalid)
                Assert.IsFalse(identity.IsValid);
        }
    }
}
