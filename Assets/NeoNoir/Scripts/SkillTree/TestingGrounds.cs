namespace LightAndShadow
{
    /// <summary>
    /// A circle in the Afterworld where the player has their blades. Stepping
    /// in arms them with the setup chosen at the <see cref="LoadoutStation"/>,
    /// the blades fading in; stepping out takes them away again, fading out.
    /// </summary>
    /// <remarks>
    /// Put a <see cref="TrainingDummy"/> inside it to hit. The player's
    /// <see cref="BladeArsenal"/> should be set to Unarmed, as it is
    /// everywhere else in the Afterworld.
    /// </remarks>
    public sealed class TestingGrounds : AfterworldCircle
    {
        private BladeArsenal arsenal;

        protected override void Entered()
        {
            arsenal = Player != null ? Player.GetComponent<BladeArsenal>() : null;

            if (arsenal == null)
            {
                return;
            }

            ChosenLoadout.Resolve(ChosenLoadout.OwnedInScene());
            arsenal.Arm(ChosenLoadout.Primary, ChosenLoadout.Secondaries);
        }

        protected override void Left()
        {
            if (arsenal != null)
            {
                arsenal.Unarm();
            }
        }
    }
}
