namespace prefSQL.SQLSkyline
{
    using System;
    using System.Collections.Generic;
    using System.Linq;

    internal sealed class SkylineSampleUtility
    {
        private HashSet<HashSet<int>> _subspaces;
        private readonly ISamplingSkylineSubspacesProducer _subspacesProducer;
        private int _subspacesCount;
        private int _subspaceDimension;
        private int _allPreferencesCount;

        internal int SubspacesCount
        {
            get { return _subspacesCount; }
            set
            {
                _subspacesCount = value;
                SubspacesProducer.SubspacesCount = value;
            }
        }

        internal int SubspaceDimension
        {
            get { return _subspaceDimension; }
            set
            {
                _subspaceDimension = value;
                SubspacesProducer.SubspaceDimension = value;
            }
        }

        internal int AllPreferencesCount
        {
            get { return _allPreferencesCount; }
            set
            {
                _allPreferencesCount = value;
                SubspacesProducer.AllPreferencesCount = value;
            }
        }

        internal ISamplingSkylineSubspacesProducer SubspacesProducer
        {
            get { return _subspacesProducer; }
        }

        public SkylineSampleUtility()
            : this(new RandomSamplingSkylineSubspacesProducer())
        {
        }

        public SkylineSampleUtility(ISamplingSkylineSubspacesProducer subspacesProducer)
        {
            _subspacesProducer = subspacesProducer;
        }

        internal HashSet<HashSet<int>> Subspaces
        {
            get { return _subspaces ?? (_subspaces = DetermineSubspaces()); }
            private set { _subspaces = value; }
        }


        private HashSet<HashSet<int>> DetermineSubspaces()
        {
            CheckValidityOfCountAndDimension(SubspacesCount, SubspaceDimension, AllPreferencesCount);

            var subspacesReturn = SubspacesProducer.GetSubspaces();

            if (subspacesReturn.Count != SubspacesCount)
            {
                throw new Exception("Not produced the correct number of subspaces.");
            }

            if (subspacesReturn.Any(subspace => subspace.Count != SubspaceDimension))
            {
                throw new Exception("Produced subspace of incorrect dimension.");
            }

            if (!AreAllPreferencesAtLeastOnceContainedInSubspaces(subspacesReturn))
            {
                throw new Exception("Not all preferences at least once contained in produced subspaces.");
            }

            foreach (var subspaceReturn in subspacesReturn)
            {
                if (
                    subspacesReturn.Where(element => element != subspaceReturn)
                        .Any(element => element.SetEquals(subspaceReturn)))
                {
                    throw new Exception("Same subspace contained multiple times.");
                }
            }

            return subspacesReturn;
        }

        /// <summary>
        ///     Checks whether the provided parameters are valid for the production of the requested subspaces.
        /// </summary>
        /// <remarks>
        ///     The provided parameters are valid if all preferences requested in the original skyline can be included in
        ///     at least one subspace (i.e., subspacesCount * subspaceDimension is larger than or equal to allPreferencesCount) and
        ///     if there are not more distinct subspaces requested than can possibly be produced (i.e., subspacesCount is lower
        ///     than or equal to the binomial coefficient ("n choose k", i.e. "allPreferencesCount choose subspaceDimension")).
        /// </remarks>
        /// <param name="subspacesCount">Number of desired subspaces.</param>
        /// <param name="subspaceDimension">Dimensionality of each subspace.</param>
        /// <param name="allPreferencesCount">Number of all preferences requested in original skyline query.</param>
        /// <exception cref="Exception">
        ///     Thrown when the provided parameters are not valid according to the conditions specified in the remarks.
        /// </exception>
        internal static void CheckValidityOfCountAndDimension(int subspacesCount, int subspaceDimension,
            int allPreferencesCount)
        {
            if (subspacesCount * subspaceDimension < allPreferencesCount)
            {
                throw new Exception(
                    string.Format(
                        "Every preference has to be included in at least one subspace. This is not possible, since there are {0} preferences and at most COUNT (= {1}) * DIMENSION (= {2}) = {3} of them are included",
                        allPreferencesCount, subspacesCount, subspaceDimension, subspacesCount * subspaceDimension));
            }

            int binomialCoefficient = BinomialCoefficient(allPreferencesCount, subspaceDimension);

            if (subspacesCount > binomialCoefficient)
            {
                throw new Exception(
                    string.Format(
                        "Cannot choose {0} from {1} in order to gain {2} subspaces, at most {3} subspaces possible.",
                        subspaceDimension, allPreferencesCount, subspacesCount, binomialCoefficient));
            }
        }

        private bool AreAllPreferencesAtLeastOnceContainedInSubspaces(
            IEnumerable<HashSet<int>> subspaceQueries)
        {
            var containedPreferences = new HashSet<int>();

            foreach (HashSet<int> subspaceQueryPreferences in subspaceQueries)
            {
                foreach (int subspaceQueryPreference in subspaceQueryPreferences)
                {
                    containedPreferences.Add(subspaceQueryPreference);
                }
                if (containedPreferences.Count == AllPreferencesCount)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     Calculate the binomial coefficient (n choose k).
        /// </summary>
        /// <remarks>
        ///     Implemented via a multiplicative formula, see
        ///     http://en.wikipedia.org/wiki/Binomial_coefficient#Multiplicative_formula
        /// </remarks>
        /// <param name="n">choose from set n</param>
        /// <param name="k">choose k elements from set n</param>
        /// <returns>Binomial coefficient from n choose k. Returns 0 if n &lt;= 0 or k &gt; n or k &lt; 0.</returns>
        internal static int BinomialCoefficient(int n, int k)
        {
            if (n <= 0)
            {
                return 0;
            }

            if (k > n || k < 0)
            {
                return 0;
            }

            var binomialCoefficient = 1;
            for (var i = 1; i <= k; i++)
            {
                binomialCoefficient *= n + 1 - i;
                binomialCoefficient /= i;
            }
            return binomialCoefficient;
        }
    }
}