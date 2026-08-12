// Tree-walking helpers for the osu.Framework drawable hierarchy.
//
// `Children` is only exposed on Container<T>; many composites (custom CompositeDrawable
// subclasses) hide their children in InternalChildren, so a plain walk misses subtrees.
// These walk InternalChildren via reflection instead, covering the whole tree.

using System;
using System.Collections.Generic;
using System.Reflection;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;

namespace osu.Game.Rulesets.MOsu.Extensions
{
    public static class DrawableExtensions
    {
        private static readonly PropertyInfo internal_children_property =
            typeof(CompositeDrawable).GetProperty("InternalChildren", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMemberException(nameof(CompositeDrawable), "InternalChildren");

        /// <summary>
        /// Returns the internal children of a drawable, or an empty list for a leaf.
        /// </summary>
        public static IReadOnlyList<Drawable>? GetInternalChildren(this Drawable drawable)
            => drawable is CompositeDrawable composite && internal_children_property.GetValue(composite) is IReadOnlyList<Drawable> children
                ? children
                : null;

        /// <summary>
        /// Finds the first descendant of the given type in this drawable's subtree.
        /// </summary>
        public static T? FindDescendant<T>(this Drawable root) where T : Drawable
        {
            if (root is T match)
                return match;

            var children = root.GetInternalChildren();

            if (children == null)
                return null;

            foreach (var child in children)
            {
                if (child.FindDescendant<T>() is T found)
                    return found;
            }

            return null;
        }

        /// <summary>
        /// Finds all descendants of the given type in this drawable's subtree.
        /// </summary>
        public static IEnumerable<T> FindDescendants<T>(this Drawable? root) where T : Drawable
        {
            if (root == null)
                yield break;

            if (root is T match)
                yield return match;

            var children = root.GetInternalChildren();

            if (children == null)
                yield break;

            foreach (var child in children)
            {
                foreach (var found in child.FindDescendants<T>())
                    yield return found;
            }
        }
    }
}
