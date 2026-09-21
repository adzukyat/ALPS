# Adzuki Live Performance System Test Fixtures

`PreviewSmoke.unity` and `PreviewSmoke.playable` are committed fixtures used by the Level 3/4 EditMode tests.

To inspect the same fixture manually:

1. Open `TestProject~` in Unity 2022.3.22f1.
2. If external packages have not been downloaded yet, run `scripts/bootstrap-test-project.sh` from the repository root first.
3. Open `Assets/AlpsTestFixtures/PreviewSmoke.unity`.
4. Scrub the `PreviewSmoke.playable` Timeline on the `PreviewSmoke Director` object.

To rebuild the fixture, run `ALPS > Tests > Regenerate Preview Smoke Fixture`.
