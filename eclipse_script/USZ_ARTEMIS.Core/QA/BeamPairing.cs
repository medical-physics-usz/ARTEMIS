using System;
using System.Collections.Generic;
using System.Linq;

namespace USZ_ARTEMIS.Core.QA
{
    public sealed class BeamPair<T>
    {
        internal BeamPair(string id, IReadOnlyList<T> selected, IReadOnlyList<T> reference)
        {
            Id = id;
            Selected = selected;
            Reference = reference;
        }

        public string Id { get; }
        public IReadOnlyList<T> Selected { get; }
        public IReadOnlyList<T> Reference { get; }
    }

    public static class BeamPairing
    {
        public static IEnumerable<BeamPair<T>> ById<T>(
            IEnumerable<T> selected, IEnumerable<T> reference, Func<T, string> getId)
        {
            var selectedById = selected.ToLookup(getId, StringComparer.Ordinal);
            var referenceById = reference.ToLookup(getId, StringComparer.Ordinal);
            var ids = selectedById.Select(group => group.Key)
                .Union(referenceById.Select(group => group.Key), StringComparer.Ordinal);

            foreach (string id in ids)
                yield return new BeamPair<T>(id, selectedById[id].ToArray(), referenceById[id].ToArray());
        }
    }
}
