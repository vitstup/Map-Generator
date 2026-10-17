using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace TacticalMaps.Generation
{
    // Managed snapshots only: terrain analysis and contour tracing can run on a CPU worker.
    internal sealed class RockEscarpmentField
    {
        internal GameObject[] Prefabs = Array.Empty<GameObject>();
        internal Bounds[] Bounds;
        internal ushort[][] Pixels;
        internal int[] Widths, Heights;
        internal Vector2[] Sizes;
        internal float Height, Sea, Slope, Relief, Spacing, SeamClearance, Burial;
        internal float MinimumWidth, MinimumSpan, MaximumRun, Overlap, EndSlope;
        internal int Limit, Seed;

        private readonly struct Candidate
        {
            internal readonly Vector2 Point;
            internal readonly float Gradient;
            internal Candidate(Vector2 point, float gradient) { Point = point; Gradient = gradient; }
        }

        private readonly struct Section
        {
            internal readonly Vector2 Centre, Anchor, ProfileCentre, Down, Tangent;
            internal readonly float Width, Depth, ProfileDepth;
            internal Section(Vector2 centre, Vector2 anchor, Vector2 profileCentre, Vector2 down, float width, float depth, float profileDepth)
            { Centre = centre; Anchor = anchor; ProfileCentre = profileCentre; Down = down; Tangent = new Vector2(-down.y, down.x); Width = width; Depth = depth; ProfileDepth = profileDepth; }
        }

        internal RockPlacement[] Render(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var output = new List<RockPlacement>();
            if (Pixels == null) return output.ToArray();
            for (int level = 0; level < 3; level++)
            {
                int outputStart = output.Count;
                float factor = Mathf.Pow(1.6f, level);
                float step = Spacing * factor, probe = step * 0.75f;
                var candidates = new List<Candidate>();
                for (float z = -Sizes[level].y * 0.5f + probe; z < Sizes[level].y * 0.5f - probe; z += step)
                {
                    token.ThrowIfCancellationRequested();
                    for (float x = -Sizes[level].x * 0.5f + probe; x < Sizes[level].x * 0.5f - probe; x += step)
                    {
                        var p = new Vector2(x, z);
                        if (!Owned(level, p, probe) || Sample(level, p) <= Sea + Relief) continue;
                        float gradient = Gradient(level, p, probe).magnitude;
                        if (gradient >= Mathf.Tan(Slope * Mathf.Deg2Rad)) candidates.Add(new Candidate(p, gradient));
                    }
                }
                // Stable priority: build complete strong escarpments before considering small features.
                candidates.Sort((a, b) => { int c = b.Gradient.CompareTo(a.Gradient); return c != 0 ? c : a.Point.y != b.Point.y ? a.Point.y.CompareTo(b.Point.y) : a.Point.x.CompareTo(b.Point.x); });
                var sections = new List<Section>();
                var random = new System.Random(unchecked(Seed + level * 104729));
                foreach (var candidate in candidates)
                {
                    token.ThrowIfCancellationRequested();
                    if (sections.Count >= Limit) break;
                    if (Covered(candidate.Point, sections, 0.55f)) continue;
                    if (!TrySection(level, candidate.Point, probe, factor, random, sections, output, token, out var first)) continue;
                    // Follow both directions along the same contour, with deliberate mesh overlap.
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        var previous = first;
                        var direction = first.Tangent * sign;
                        for (int i = 0; i < 128 && sections.Count < Limit; i++)
                        {
                            token.ThrowIfCancellationRequested();
                            var next = previous.Anchor + direction * previous.Width * (1 - Overlap);
                            if (!TrySection(level, next, probe, factor, random, sections, output, token, out var section)) break;
                            var tangent = section.Tangent;
                            if (Vector2.Dot(tangent, direction) < 0) tangent = -tangent;
                            if (Vector2.Dot(tangent, direction) < 0.45f) break;
                            previous = section; direction = tangent;
                        }
                    }
                }
                RemoveShortFeatures(sections, output, outputStart, MinimumSpan * factor);
            }
            return output.ToArray();
        }

        private void RemoveShortFeatures(List<Section> sections, List<RockPlacement> output, int outputStart, float minimumSpan)
        {
            var visited = new bool[sections.Count]; var keep = new bool[sections.Count];
            for (int start = 0; start < sections.Count; start++)
            {
                if (visited[start]) continue;
                var group = new List<int> { start }; visited[start] = true;
                float span = 0, largest = 0;
                for (int cursor = 0; cursor < group.Count; cursor++)
                {
                    var a = sections[group[cursor]]; span += a.Width * (1 - Overlap); largest = Mathf.Max(largest, a.Width);
                    for (int next = 0; next < sections.Count; next++)
                    {
                        if (visited[next]) continue;
                        var b = sections[next]; var delta = b.Centre - a.Centre;
                        if (delta.magnitude > (a.Width + b.Width) * 0.65f) continue;
                        if (Mathf.Abs(Vector2.Dot(a.Down, b.Down)) < 0.4f) continue;
                        visited[next] = true; group.Add(next);
                    }
                }
                if (span + largest * Overlap >= minimumSpan) foreach (int index in group) keep[index] = true;
            }
            for (int i = sections.Count - 1; i >= 0; i--) if (!keep[i]) output.RemoveAt(outputStart + i);
        }

        private bool TrySection(int level, Vector2 seed, float probe, float factor, System.Random random,
            List<Section> sections, List<RockPlacement> output, CancellationToken token, out Section section)
        {
            section = default;
            if (!Owned(level, seed, probe)) return false;
            var gradient = Gradient(level, seed, probe);
            float steep = gradient.magnitude;
            if (steep < Mathf.Tan(Slope * Mathf.Deg2Rad)) return false;
            var down = -gradient / steep;
            // Locate the steep centre of a broad slope, independently of the section dimensions.
            float best = steep; var point = seed;
            for (int i = -4; i <= 4; i++)
            {
                var p = seed + down * (i * probe * 0.5f);
                if (!Owned(level, p, probe)) continue;
                float value = Gradient(level, p, probe).magnitude;
                if (value > best) { best = value; point = p; }
            }
            gradient = Gradient(level, point, probe); down = -gradient.normalized;
            float endGradient = Mathf.Max(Mathf.Tan(EndSlope * Mathf.Deg2Rad), gradient.magnitude * 0.12f);
            var crest = FindEnd(level, point, -down, probe, factor, endGradient, true, token);
            var toe = FindEnd(level, point, down, probe, factor, endGradient, false, token);
            float high = Sample(level, crest), low = Sample(level, toe), drop = high - low;
            if (drop < Relief || low <= Sea + 2) return false;
            float run = Vector2.Distance(crest, toe);
            if (run < probe) return false;
            var tangent = new Vector2(-down.y, down.x);
            int prefab = random.Next(Prefabs.Length);
            var broad = new List<int>();
            for (int i = 0; i < Bounds.Length; i++) if (Bounds[i].size.x / Bounds[i].size.y >= 0.85f) broad.Add(i);
            if (broad.Count > 0) prefab = broad[random.Next(broad.Count)];
            var bounds = Bounds[prefab];
            float top = high - drop * 0.04f, bottom = low - drop * Mathf.Max(0.3f, Burial);
            float sy = (top - bottom) / bounds.size.y;
            sy = Mathf.Max(sy, MinimumWidth * factor / (bounds.size.x * 1.25f));
            bottom = top - sy * bounds.size.y;
            float width = Mathf.Clamp(Mathf.Max(MinimumWidth * factor, drop * 2.2f), bounds.size.x * sy * 0.75f, bounds.size.x * sy * 1.25f);
            // Do not let a broad section bridge across an unrelated valley or over the coastline.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                var shift = tangent * width * 0.38f;
                if (Sample(level, crest + shift) >= high - drop * 0.35f && Sample(level, crest - shift) >= high - drop * 0.35f &&
                    Sample(level, toe + shift) > Sea + 2 && Sample(level, toe - shift) > Sea + 2) break;
                width *= 0.8f;
            }
            if (width < MinimumWidth * factor * 0.6f) return false;
            // Keep the authored closed massif shape. Its back enters the upper hillside;
            // placing it at the toe instead makes a disconnected freestanding mesa.
            float depth = Mathf.Clamp(run, bounds.size.z * sy * 0.75f, bounds.size.z * sy * 1.4f);
            var centre = crest + down * depth * 0.38f;
            // A large buried mesh can still look like a small grass-covered boulder.
            // Require a substantial exposed face, not just a large transform scale.
            float exposed = top - Sample(level, centre + down * depth * 0.35f);
            if (exposed < Mathf.Max(Relief * 0.6f, drop * 0.2f)) return false;
            section = new Section(centre, point, (crest + toe) * 0.5f, down, width, depth, run);
            if (!FootprintOwned(level, section) || Overlaps(section, sections)) return false;
            // Full crest-to-foot height, with a buried foundation. No small-prefab scale ceiling.
            var scale = new Vector3(width / bounds.size.x, (top - bottom) / bounds.size.y, depth / bounds.size.z);
            var rotation = Quaternion.LookRotation(new Vector3(down.x, 0, down.y));
            var position = new Vector3(centre.x, bottom - bounds.min.y * scale.y, centre.y)
                - rotation * new Vector3(bounds.center.x * scale.x, 0, bounds.center.z * scale.z);
            output.Add(new RockPlacement(level, prefab, position, rotation, scale));
            sections.Add(section);
            return true;
        }

        private Vector2 FindEnd(int level, Vector2 start, Vector2 direction, float probe, float factor,
            float endGradient, bool uphill, CancellationToken token)
        {
            var last = start; float height = Sample(level, start);
            int gentle = 0;
            float stride = Mathf.Max(4, probe * 0.5f);
            for (float distance = stride; distance <= MaximumRun * factor * 0.5f; distance += stride)
            {
                token.ThrowIfCancellationRequested();
                var p = start + direction * distance;
                if (!Owned(level, p, probe * 0.5f)) break;
                float next = Sample(level, p), rise = (next - height) * (uphill ? 1 : -1);
                if (next <= Sea + 2 || rise < -stride * 0.04f) break;
                last = p; height = next;
                gentle = rise / stride < endGradient ? gentle + 1 : 0;
                if (gentle >= 2) break;
            }
            return last;
        }

        private static bool Covered(Vector2 point, List<Section> sections, float fraction)
        {
            foreach (var section in sections)
            {
                var delta = point - section.ProfileCentre;
                if (Mathf.Abs(Vector2.Dot(delta, section.Down)) < section.ProfileDepth * 0.75f &&
                    Mathf.Abs(Vector2.Dot(delta, section.Tangent)) < section.Width * fraction) return true;
            }
            return false;
        }

        private static bool Overlaps(Section next, List<Section> sections)
        {
            foreach (var previous in sections)
            {
                var delta = next.ProfileCentre - previous.ProfileCentre;
                if (Mathf.Abs(Vector2.Dot(next.Down, previous.Down)) > 0.65f)
                {
                    if (Mathf.Abs(Vector2.Dot(delta, previous.Down)) < (next.ProfileDepth + previous.ProfileDepth) * 0.45f &&
                        Mathf.Abs(Vector2.Dot(delta, previous.Tangent)) < (next.Width + previous.Width) * 0.18f) return true;
                }
                else if (delta.magnitude < Mathf.Min(next.Width, previous.Width) * 0.4f) return true;
            }
            return false;
        }

        private bool FootprintOwned(int level, Section section)
        {
            var extent = new Vector2(Mathf.Abs(section.Tangent.x) * section.Width + Mathf.Abs(section.Down.x) * section.Depth,
                Mathf.Abs(section.Tangent.y) * section.Width + Mathf.Abs(section.Down.y) * section.Depth) * 0.5f;
            if (Mathf.Abs(section.Centre.x) + extent.x >= Sizes[level].x * 0.5f || Mathf.Abs(section.Centre.y) + extent.y >= Sizes[level].y * 0.5f) return false;
            if (level == 0) return true;
            return Mathf.Abs(section.Centre.x) - extent.x >= Sizes[level - 1].x * 0.5f + SeamClearance ||
                Mathf.Abs(section.Centre.y) - extent.y >= Sizes[level - 1].y * 0.5f + SeamClearance;
        }

        private bool Owned(int level, Vector2 p, float margin)
        {
            if (Mathf.Abs(p.x) > Sizes[level].x * 0.5f - margin || Mathf.Abs(p.y) > Sizes[level].y * 0.5f - margin) return false;
            return level == 0 || Mathf.Abs(p.x) >= Sizes[level - 1].x * 0.5f + SeamClearance + margin ||
                Mathf.Abs(p.y) >= Sizes[level - 1].y * 0.5f + SeamClearance + margin;
        }

        private Vector2 Gradient(int level, Vector2 p, float radius)
            => new Vector2(Sample(level, p + Vector2.right * radius) - Sample(level, p - Vector2.right * radius),
                Sample(level, p + Vector2.up * radius) - Sample(level, p - Vector2.up * radius)) / (2 * radius);

        private float Sample(int level, Vector2 p)
        {
            float u = Mathf.Clamp01(p.x / Sizes[level].x + 0.5f) * (Widths[level] - 1);
            float v = Mathf.Clamp01(p.y / Sizes[level].y + 0.5f) * (Heights[level] - 1);
            int x = Mathf.Min((int)u, Widths[level] - 2), z = Mathf.Min((int)v, Heights[level] - 2), w = Widths[level];
            var raw = Pixels[level];
            return Mathf.Lerp(Mathf.Lerp(raw[z * w + x], raw[z * w + x + 1], u - x),
                Mathf.Lerp(raw[(z + 1) * w + x], raw[(z + 1) * w + x + 1], u - x), v - z) / 65535f * Height;
        }
    }
}
