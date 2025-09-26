using System.Collections.Generic;
using System.Linq;

namespace Kiryonn.Searchables
{
	/// <summary>
	/// A static utility class for performing fuzzy string searches.
	/// The search algorithm scores strings based on character matches, rewarding contiguous matches and penalizing gaps.
	/// </summary>
	public static class FuzzySearch
	{
		public static List<string> Search(IEnumerable<string> choices, string query)
		{
			if (string.IsNullOrEmpty(query))
			{
				return choices.ToList();
			}

			var results = new List<(string item, int score)>();
			var lowerQuery = query.ToLower();

			foreach (var item in choices)
			{
				var score = CalculateScore(item.ToLower(), lowerQuery);
				if (score > 0)
				{
					results.Add((item, score));
				}
			}
			return results.OrderByDescending(r => r.score).Select(r => r.item).ToList();
		}

		private static int CalculateScore(string item, string query)
		{
			int score = 0;
			int queryIndex = 0;

			for (int i = 0; i < item.Length; i++)
			{
				if (queryIndex < query.Length && item[i] == query[queryIndex])
				{
					score += 10; // Base score for a match
					queryIndex++;

					if (queryIndex > 0)
					{
						score += 5; // Bonus for a contiguous match
					}
				}
				else
				{
					score -= 1; // Penalty for a non-matching character
				}
			}

			// Final penalty if not all query characters were found
			if (queryIndex != query.Length)
			{
				score = 0;
			}

			return score;
		}
	}
}
