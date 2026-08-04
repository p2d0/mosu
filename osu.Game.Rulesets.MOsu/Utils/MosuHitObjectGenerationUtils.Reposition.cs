// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Logging;
using osu.Framework.Utils;
using osu.Game.Rulesets.Osu.Objects;
using osu.Game.Rulesets.Osu.UI;
using osuTK;

namespace osu.Game.Rulesets.MOsu.Utils
{
    public static partial class MosuHitObjectGenerationUtils
    {
        private static readonly Vector2 playfield_centre = OsuPlayfield.BASE_SIZE / 2;

        /// <summary>
        /// Generate a list of <see cref="ObjectPositionInfo"/>s containing information for how the given list of
        /// <see cref="OsuHitObject"/>s are positioned.
        /// </summary>
        /// <param name="hitObjects">A list of <see cref="OsuHitObject"/>s to process.</param>
        /// <returns>A list of <see cref="ObjectPositionInfo"/>s describing how each hit object is positioned relative to the previous one.</returns>
        public static List<ObjectPositionInfo> GeneratePositionInfos(IEnumerable<OsuHitObject> hitObjects)
        {
            var positionInfos = new List<ObjectPositionInfo>();
            Vector2 previousPosition = playfield_centre;
            float previousAngle = 0;

            foreach (OsuHitObject hitObject in hitObjects)
            {
                Vector2 relativePosition = hitObject.Position - previousPosition;
                float absoluteAngle = MathF.Atan2(relativePosition.Y, relativePosition.X);
                float relativeAngle = absoluteAngle - previousAngle;

                ObjectPositionInfo positionInfo;
                positionInfos.Add(positionInfo = new ObjectPositionInfo(hitObject)
                {
                    RelativeAngle = relativeAngle,
                    DistanceFromPrevious = relativePosition.Length
                });

                if (hitObject is Slider slider)
                {
                    float absoluteRotation = getSliderRotation(slider);
                    positionInfo.Rotation = absoluteRotation - absoluteAngle;
                    absoluteAngle = absoluteRotation;
                }

                previousPosition = hitObject.EndPosition;
                previousAngle = absoluteAngle;
            }

            return positionInfos;
        }

        /// <summary>
        /// Scale distances between hit objects using <paramref name="objectPositionInfos"/>,
        /// apply edge-aware rotation via <see cref="RotateAwayFromEdge"/>,
        /// then only clamp objects that fall outside the playfield.
        /// Uses original direction vectors to avoid drift when distances unchanged.
        /// Does NOT shift preceding objects. No padding (hardcore-style).
        /// </summary>
        /// <param name="objectPositionInfos">Position information with (potentially) modified distances.</param>
        /// <returns>The repositioned hit objects.</returns>
        public static List<OsuHitObject> RepositionHitObjectsClampOnly(IEnumerable<ObjectPositionInfo> objectPositionInfos)
        {
            Vector2 originalPreviousEndPosition = playfield_centre;
            Vector2 newPreviousEndPosition = playfield_centre;

            foreach (var info in objectPositionInfos)
            {
                var hitObject = info.HitObject;

                if (hitObject is Spinner)
                {
                    originalPreviousEndPosition = hitObject.EndPosition;
                    newPreviousEndPosition = hitObject.EndPosition;
                    continue;
                }

                // Original direction from ORIGINAL previous end position (not the new one)
                Vector2 originalPos = hitObject.Position;
                Vector2 originalDirection = originalPos - originalPreviousEndPosition;
                float originalDistance = originalDirection.Length;

                bool distanceChanged = !Precision.AlmostEquals(info.DistanceFromPrevious, originalDistance);

                Vector2 posRelativeToPrev;
                float rotationRatio = 0f;
                if (originalDistance > 0)
                {
                    // Scale direction by new distance (no drift when distance unchanged)
                    posRelativeToPrev = originalDirection * (info.DistanceFromPrevious / originalDistance);
                    // Ramp rotation smoothly from 0 (tiny change) up to the original 0.5 cap (large change),
                    // so a 0.001 spacing tweak barely rotates while spacing 3 behaves like before.
                    rotationRatio = 0.5f * MathF.Min(1f, MathF.Abs(info.DistanceFromPrevious / originalDistance - 1f));
                }
                else
                {
                    // Degenerate
                    float angle = MathF.Atan2(originalDirection.Y, originalDirection.X);
                    posRelativeToPrev = new Vector2(
                        info.DistanceFromPrevious * MathF.Cos(angle),
                        info.DistanceFromPrevious * MathF.Sin(angle)
                    );
                    rotationRatio = 0.5f;
                }

                // Only apply edge-aware rotation when distance changed, ramped smoothly
                // (no hard switch, no over-rotation at large spacings).
                if (distanceChanged && rotationRatio > 0)
                    posRelativeToPrev = RotateAwayFromEdge(newPreviousEndPosition, posRelativeToPrev, rotationRatio);

                Vector2 newPos = newPreviousEndPosition + posRelativeToPrev;

                if (hitObject is HitCircle)
                {
                    Vector2 finalPos = newPos;

                    // No padding (hardcore-style)
                    if (newPos.X < 0 || newPos.X > OsuPlayfield.BASE_SIZE.X ||
                        newPos.Y < 0 || newPos.Y > OsuPlayfield.BASE_SIZE.Y)
                    {
                        finalPos = new Vector2(
                            Math.Clamp(newPos.X, 0, OsuPlayfield.BASE_SIZE.X),
                            Math.Clamp(newPos.Y, 0, OsuPlayfield.BASE_SIZE.Y)
                        );
                    }

                    hitObject.Position = finalPos;
                    originalPreviousEndPosition = originalPos;
                    newPreviousEndPosition = finalPos;
                }
                else if (hitObject is Slider slider)
                {
                    slider.Position = newPos;

                    var bounds = CalculatePossibleMovementBounds(slider);
                    if (bounds.Width < 0 || bounds.Height < 0)
                    {
                        float currentRot = getSliderRotation(slider);
                        float origRot = getSliderRotation(slider);
                        float diff1 = getAngleDifference(origRot, currentRot);
                        float diff2 = getAngleDifference(origRot + MathF.PI, currentRot);
                        if (diff1 < diff2)
                            RotateSlider(slider, origRot - getSliderRotation(slider));
                        else
                            RotateSlider(slider, origRot + MathF.PI - getSliderRotation(slider));
                        bounds = CalculatePossibleMovementBounds(slider);
                    }

                    var sliderPos = slider.Position;
                    if (sliderPos.X < bounds.Left || sliderPos.X > bounds.Right ||
                        sliderPos.Y < bounds.Top || sliderPos.Y > bounds.Bottom)
                    {
                        float newX = bounds.Width < 0
                            ? Math.Clamp(bounds.Left, 0, OsuPlayfield.BASE_SIZE.X)
                            : Math.Clamp(sliderPos.X, bounds.Left, bounds.Right);
                        float newY = bounds.Height < 0
                            ? Math.Clamp(bounds.Top, 0, OsuPlayfield.BASE_SIZE.Y)
                            : Math.Clamp(sliderPos.Y, bounds.Top, bounds.Bottom);
                        slider.Position = new Vector2(newX, newY);
                    }

                    originalPreviousEndPosition = slider.EndPosition;
                    newPreviousEndPosition = slider.EndPosition;
                }
            }

            return objectPositionInfos.Select(p => p.HitObject).ToList();
        }

        /// <summary>
        /// Reposition the hit objects according to the information in <paramref name="objectPositionInfos"/>.
        /// No padding (hardcore-style).
        /// </summary>
        /// <param name="objectPositionInfos">Position information for each hit object.</param>
        /// <param name="extendPlayArea">Extend Play area</param>
        /// <param name="infinitePlayArea">Infinite play area</param>
        /// <returns>The repositioned hit objects.</returns>
        public static List<OsuHitObject> RepositionHitObjects(IEnumerable<ObjectPositionInfo> objectPositionInfos,
                                                              bool extendPlayArea = false,
                                                              bool infinitePlayArea = false
        )
        {
            List<WorkingObject> workingObjects = objectPositionInfos.Select(o => new WorkingObject(o)).ToList();
            WorkingObject? previous = null;

            for (int i = 0; i < workingObjects.Count; i++)
            {
                var current = workingObjects[i];
                var hitObject = current.HitObject;

                if (hitObject is Spinner)
                {
                    previous = current;
                    continue;
                }

                computeModifiedPosition(current, previous, i > 1 ? workingObjects[i - 2] : null);

                // Move hit objects back into the playfield if they are outside of it
                Vector2 shift = Vector2.Zero;

                switch (hitObject)
                {
                    case HitCircle:
                        shift = clampHitCircleToPlayfield(current, extendPlayArea, infinitePlayArea);
                        break;

                    case Slider:
                        shift = clampSliderToPlayfield(current);
                        break;
                }



                previous = current;
            }

            return workingObjects.Select(p => p.HitObject).ToList();
        }

        /// <summary>
        /// Compute the modified position of a hit object while attempting to keep it inside the playfield.
        /// </summary>
        /// <param name="current">The <see cref="WorkingObject"/> representing the hit object to have the modified position computed for.</param>
        /// <param name="previous">The <see cref="WorkingObject"/> representing the hit object immediately preceding the current one.</param>
        /// <param name="beforePrevious">The <see cref="WorkingObject"/> representing the hit object immediately preceding the <paramref name="previous"/> one.</param>
        private static void computeModifiedPosition(WorkingObject current, WorkingObject? previous, WorkingObject? beforePrevious)
        {
            float previousAbsoluteAngle = 0f;

            if (previous != null)
            {
                if (previous.HitObject is Slider s)
                {
                    previousAbsoluteAngle = getSliderRotation(s);
                }
                else
                {
                    Vector2 earliestPosition = beforePrevious?.HitObject.EndPosition ?? playfield_centre;
                    Vector2 relativePosition = previous.HitObject.Position - earliestPosition;
                    previousAbsoluteAngle = MathF.Atan2(relativePosition.Y, relativePosition.X);
                }
            }

            float absoluteAngle = previousAbsoluteAngle + current.PositionInfo.RelativeAngle;

            var posRelativeToPrev = new Vector2(
                current.PositionInfo.DistanceFromPrevious * MathF.Cos(absoluteAngle),
                current.PositionInfo.DistanceFromPrevious * MathF.Sin(absoluteAngle)
            );

            Vector2 lastEndPosition = previous?.EndPositionModified ?? playfield_centre;

            posRelativeToPrev = RotateAwayFromEdge(lastEndPosition, posRelativeToPrev);

            current.PositionModified = lastEndPosition + posRelativeToPrev;

            if (!(current.HitObject is Slider slider))
                return;

            absoluteAngle = MathF.Atan2(posRelativeToPrev.Y, posRelativeToPrev.X);

            Vector2 centreOfMassOriginal = calculateCentreOfMass(slider);
            Vector2 centreOfMassModified = rotateVector(centreOfMassOriginal, current.PositionInfo.Rotation + absoluteAngle - getSliderRotation(slider));
            centreOfMassModified = RotateAwayFromEdge(current.PositionModified, centreOfMassModified);

            float relativeRotation = MathF.Atan2(centreOfMassModified.Y, centreOfMassModified.X) - MathF.Atan2(centreOfMassOriginal.Y, centreOfMassOriginal.X);
            if (!Precision.AlmostEquals(relativeRotation, 0))
                RotateSlider(slider, relativeRotation);
        }

        /// <summary>
        /// Move the modified position of a <see cref="HitCircle"/> so that it fits inside the playfield.
        /// </summary>
        /// <returns>The deviation from the original modified position in order to fit within the playfield.</returns>
        private static Vector2 clampHitCircleToPlayfield(WorkingObject workingObject, bool extendPlayArea = false, bool infinitePlayArea = false)
        {
            var previousPosition = workingObject.PositionModified;
            if(!infinitePlayArea && !extendPlayArea)
                workingObject.EndPositionModified = workingObject.PositionModified = ClampToPlayfieldWithPadding(
                    workingObject.PositionModified,
                    0f
                );

            if(extendPlayArea)
                workingObject.EndPositionModified = workingObject.PositionModified = new Vector2(
                Math.Clamp(workingObject.PositionModified.X, 0, OsuPlayfield.BASE_SIZE.X + 40),
                Math.Clamp(workingObject.PositionModified.Y, 0, OsuPlayfield.BASE_SIZE.Y + 30)
            );


            workingObject.HitObject.Position = workingObject.PositionModified;

            return workingObject.PositionModified - previousPosition;
        }

        /// <summary>
        /// Moves the <see cref="Slider"/> and all necessary nested <see cref="OsuHitObject"/>s into the <see cref="OsuPlayfield"/> if they aren't already.
        /// </summary>
        /// <returns>The deviation from the original modified position in order to fit within the playfield.</returns>
        private static Vector2 clampSliderToPlayfield(WorkingObject workingObject)
        {
            var slider = (Slider)workingObject.HitObject;
            var possibleMovementBounds = CalculatePossibleMovementBounds(slider);

            // The slider rotation applied in computeModifiedPosition might make it impossible to fit the slider into the playfield
            // For example, a long horizontal slider will be off-screen when rotated by 90 degrees
            // In this case, limit the rotation to either 0 or 180 degrees
            if (possibleMovementBounds.Width < 0 || possibleMovementBounds.Height < 0)
            {
                float currentRotation = getSliderRotation(slider);
                float diff1 = getAngleDifference(workingObject.RotationOriginal, currentRotation);
                float diff2 = getAngleDifference(workingObject.RotationOriginal + MathF.PI, currentRotation);

                if (diff1 < diff2)
                {
                    RotateSlider(slider, workingObject.RotationOriginal - getSliderRotation(slider));
                }
                else
                {
                    RotateSlider(slider, workingObject.RotationOriginal + MathF.PI - getSliderRotation(slider));
                }

                possibleMovementBounds = CalculatePossibleMovementBounds(slider);
            }

            var previousPosition = workingObject.PositionModified;

            // Clamp slider position to the placement area
            // If the slider is larger than the playfield, at least make sure that the head circle is inside the playfield
            float newX = possibleMovementBounds.Width < 0
                ? Math.Clamp(possibleMovementBounds.Left, 0, OsuPlayfield.BASE_SIZE.X)
                : Math.Clamp(previousPosition.X, possibleMovementBounds.Left, possibleMovementBounds.Right);

            float newY = possibleMovementBounds.Height < 0
                ? Math.Clamp(possibleMovementBounds.Top, 0, OsuPlayfield.BASE_SIZE.Y)
                : Math.Clamp(previousPosition.Y, possibleMovementBounds.Top, possibleMovementBounds.Bottom);

            slider.Position = workingObject.PositionModified = new Vector2(newX, newY);
            workingObject.EndPositionModified = slider.EndPosition;

            return workingObject.PositionModified - previousPosition;
        }

        /// <summary>
        /// Calculates a <see cref="RectangleF"/> which contains all of the possible movements of the slider (in relative X/Y coordinates)
        /// such that the entire slider is inside the playfield.
        /// </summary>
        /// <param name="slider">The <see cref="Slider"/> for which to calculate a movement bounding box.</param>
        /// <returns>A <see cref="RectangleF"/> which contains all of the possible movements of the slider such that the entire slider is inside the playfield.</returns>
        /// <remarks>
        /// If the slider is larger than the playfield, the returned <see cref="RectangleF"/> may have negative width/height.
        /// </remarks>
        public static RectangleF CalculatePossibleMovementBounds(Slider slider)
        {
            var pathPositions = new List<Vector2>();
            slider.Path.GetPathToProgress(pathPositions, 0, 1);

            float minX = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;

            float minY = float.PositiveInfinity;
            float maxY = float.NegativeInfinity;

            // Compute the bounding box of the slider.
            foreach (var pos in pathPositions)
            {
                minX = MathF.Min(minX, pos.X);
                maxX = MathF.Max(maxX, pos.X);

                minY = MathF.Min(minY, pos.Y);
                maxY = MathF.Max(maxY, pos.Y);
            }

            // Take the circle radius into account.
            float radius = (float)slider.Radius;

            minX -= radius;
            minY -= radius;

            maxX += radius;
            maxY += radius;

            // Given the bounding box of the slider (via min/max X/Y),
            // the amount that the slider can move to the left is minX (with the sign flipped, since positive X is to the right),
            // and the amount that it can move to the right is WIDTH - maxX.
            // Same calculation applies for the Y axis.
            float left = -minX;
            float right = OsuPlayfield.BASE_SIZE.X - maxX;
            float top = -minY;
            float bottom = OsuPlayfield.BASE_SIZE.Y - maxY;

            return new RectangleF(left, top, right - left, bottom - top);
        }

        /// <summary>
        /// Clamp a position to playfield, keeping a specified distance from the edges.
        /// </summary>
        /// <param name="position">The position to be clamped.</param>
        /// <param name="padding">The minimum distance allowed from playfield edges.</param>
        /// <returns>The clamped position.</returns>
        public static Vector2 ClampToPlayfieldWithPadding(Vector2 position, float padding)
        {
            return new Vector2(
                Math.Clamp(position.X, padding, OsuPlayfield.BASE_SIZE.X - padding),
                Math.Clamp(position.Y, padding, OsuPlayfield.BASE_SIZE.Y - padding)
            );
        }

        /// <summary>
        /// Estimate the centre of mass of a slider relative to its start position.
        /// </summary>
        /// <param name="slider">The slider to process.</param>
        /// <returns>The centre of mass of the slider.</returns>
        private static Vector2 calculateCentreOfMass(Slider slider)
        {
            const double sample_step = 50;

            // just sample the start and end positions if the slider is too short
            if (slider.Distance <= sample_step)
            {
                return Vector2.Divide(slider.Path.PositionAt(1), 2);
            }

            int count = 0;
            Vector2 sum = Vector2.Zero;
            double pathDistance = slider.Distance;

            for (double i = 0; i < pathDistance; i += sample_step)
            {
                sum += slider.Path.PositionAt(i / pathDistance);
                count++;
            }

            return sum / count;
        }

        /// <summary>
        /// Get the absolute rotation of a slider, defined as the angle from its start position to the end of its path.
        /// </summary>
        /// <param name="slider">The slider to process.</param>
        /// <returns>The angle in radians.</returns>
        private static float getSliderRotation(Slider slider)
        {
            var endPositionVector = slider.Path.PositionAt(1);
            return MathF.Atan2(endPositionVector.Y, endPositionVector.X);
        }

        /// <summary>
        /// Get the absolute difference between 2 angles measured in Radians.
        /// </summary>
        /// <param name="angle1">The first angle</param>
        /// <param name="angle2">The second angle</param>
        /// <returns>The absolute difference with interval <c>[0, MathF.PI)</c></returns>
        private static float getAngleDifference(float angle1, float angle2)
        {
            float diff = MathF.Abs(angle1 - angle2) % (MathF.PI * 2);
            return MathF.Min(diff, MathF.PI * 2 - diff);
        }

        public class ObjectPositionInfo
        {
            /// <summary>
            /// The jump angle from the previous hit object to this one, relative to the previous hit object's jump angle.
            /// </summary>
            /// <remarks>
            /// <see cref="RelativeAngle"/> of the first hit object in a beatmap represents the absolute angle from playfield center to the object.
            /// </remarks>
            /// <example>
            /// If <see cref="RelativeAngle"/> is 0, the player's cursor doesn't need to change its direction of movement when passing
            /// the previous object to reach this one.
            /// </example>
            public float RelativeAngle { get; set; }

            /// <summary>
            /// The jump distance from the previous hit object to this one.
            /// </summary>
            /// <remarks>
            /// <see cref="DistanceFromPrevious"/> of the first hit object in a beatmap is relative to the playfield center.
            /// </remarks>
            public float DistanceFromPrevious { get; set; }

            /// <summary>
            /// The rotation of the hit object, relative to its jump angle.
            /// For sliders, this is defined as the angle from the slider's start position to the end of its path, relative to its jump angle.
            /// For hit circles and spinners, this property is ignored.
            /// </summary>
            public float Rotation { get; set; }

            /// <summary>
            /// The hit object associated with this <see cref="ObjectPositionInfo"/>.
            /// </summary>
            public OsuHitObject HitObject { get; }

            public ObjectPositionInfo(OsuHitObject hitObject)
            {
                HitObject = hitObject;
            }
        }

        private class WorkingObject
        {
            public float RotationOriginal { get; }
            public Vector2 PositionModified { get; set; }
            public Vector2 EndPositionModified { get; set; }

            public ObjectPositionInfo PositionInfo { get; }
            public OsuHitObject HitObject => PositionInfo.HitObject;

            public WorkingObject(ObjectPositionInfo positionInfo)
            {
                PositionInfo = positionInfo;
                RotationOriginal = HitObject is Slider slider ? getSliderRotation(slider) : 0;
                PositionModified = HitObject.Position;
                EndPositionModified = HitObject.EndPosition;
            }
        }
    }
}
