namespace SoundFingerprinting.Data
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using ProtoBuf;

    [ProtoContract]
    public class TimedHashes
    {
        private const double Accuracy = 1.48d;
        private const double FingerprintCount = 8192.0d / 5512;
        
        [ProtoMember(1)]
        private readonly List<HashedFingerprint> hashedFingerprints = new List<HashedFingerprint>();
        
        public TimedHashes(List<HashedFingerprint> hashedFingerprints, DateTime startsAt)
        {
            this.hashedFingerprints = hashedFingerprints;
            StartsAt = startsAt;
        }

        private TimedHashes()
        {
            // left for proto-buf
        }

        public static TimedHashes Empty => new TimedHashes(new List<HashedFingerprint>(), DateTime.MinValue);

        public IList<HashedFingerprint> HashedFingerprints => hashedFingerprints;

        [ProtoMember(2)]
        public DateTime StartsAt { get; }

        public DateTime EndsAt => IsEmpty ? DateTime.MinValue : StartsAt.Add(TimeSpan.FromSeconds(hashedFingerprints.Last().StartsAt + FingerprintCount));

        public double TotalSeconds
        {
            get
            {
                if (IsEmpty)
                {
                    return 0d;
                }

                return hashedFingerprints.Last().StartsAt + FingerprintCount;
            }
        }

        public bool IsEmpty => !hashedFingerprints.Any();

        public bool MergeWith(TimedHashes with, out TimedHashes merged)
        {
            merged = null;

            if (IsEmpty)
            {
                merged = with;
                return true;
            }
            
            if (StartsAt <= with.StartsAt && EndsAt >= with.StartsAt.Subtract(TimeSpan.FromSeconds(Accuracy)))
            {
                var result = Merge(hashedFingerprints.OrderBy(h => h.SequenceNumber).ToList(), StartsAt, with.hashedFingerprints.OrderBy(h => h.SequenceNumber).ToList(), with.StartsAt);
                merged = new TimedHashes(result, StartsAt);
                return true;
            }
            
            return false;
        }

        public static IEnumerable<TimedHashes> Aggregate(IEnumerable<TimedHashes> hashes, double length)
        {
            return hashes
                .OrderBy(entry => entry.StartsAt)
                .Aggregate(new Stack<TimedHashes>(new[] {Empty}), (stack, next) =>
                    {
                        var completed = stack.Pop();
                        if (completed.MergeWith(next, out var merged))
                        {
                            stack.Push(merged);
                            if (merged.TotalSeconds >= length)
                            {
                                stack.Push(Empty);
                            }
                        }
                        else
                        {
                            stack.Push(completed);
                            stack.Push(next);
                        }

                        return stack;
                    },
                    stack =>
                    {
                        var result = new List<TimedHashes>();
                        while (stack.Any())
                        {
                            result.Add(stack.Pop());
                        }

                        return result;
                    });
        }

        private static List<HashedFingerprint> Merge(IReadOnlyList<HashedFingerprint> first, DateTime firstStartsAt, IReadOnlyList<HashedFingerprint> second, DateTime secondStartsAt)
        {
            var result = new List<HashedFingerprint>();
            int i = 0, j = 0;
            var diff = secondStartsAt.Subtract(firstStartsAt);
            
            for (int k = 0; k < first.Count + second.Count; ++k)
            {
                if (i == first.Count)
                {
                    var startAt = diff.TotalSeconds + second[j].StartsAt;
                    result.Add(new HashedFingerprint(second[j].HashBins, (uint)k, (float)startAt, second[j].Clusters));
                    ++j;
                }
                else if (j == second.Count)
                {
                    result.Add(new HashedFingerprint(first[i].HashBins, (uint)k, first[i].StartsAt, first[i].Clusters));
                    ++i;
                }
                else if (firstStartsAt.AddSeconds(first[i].StartsAt) <= secondStartsAt.AddSeconds(second[j].StartsAt))
                {
                    result.Add(new HashedFingerprint(first[i].HashBins, (uint)k, first[i].StartsAt, first[i].Clusters));
                    ++i;
                }
                else
                {
                    var startAt = diff.TotalSeconds + second[j].StartsAt;
                    result.Add(new HashedFingerprint(second[j].HashBins, (uint)k, (float)startAt, second[j].Clusters));
                    ++j;
                }
            }

            return result;
        }
    }
}