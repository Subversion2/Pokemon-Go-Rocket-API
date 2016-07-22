using PokemonGo.RocketAPI.GeneratedCode;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PokemonGo.RocketAPI.Logic
{
    public static class Navigation
    {
        public static double DistanceBetween2Coordinates(double Lat1, double Lng1, double Lat2, double Lng2)
        {
            double r_earth = 6378137;
            double d_lat = (Lat2 - Lat1) * Math.PI / 180;
            double d_lon = (Lng2 - Lng1) * Math.PI / 180;
            double alpha = Math.Sin(d_lat / 2) * Math.Sin(d_lat / 2)
                + Math.Cos(Lat1 * Math.PI / 180) * Math.Cos(Lat2 * Math.PI / 180)
                * Math.Sin(d_lon / 2) * Math.Sin(d_lon / 2);
            double d = 2 * r_earth * Math.Atan2(Math.Sqrt(alpha), Math.Sqrt(1 - alpha));
            return d;
        }
        
        private static double getWeight(List<FortData> nodes)
        {
            double weight = 0;
            FortData previousNode = nodes.First();
            foreach (FortData node in nodes)
            {
                weight += DistanceBetween2Coordinates(previousNode.Latitude, previousNode.Longitude, node.Latitude, node.Longitude);
                previousNode = node;
             }
             weight += DistanceBetween2Coordinates(nodes.First().Latitude, nodes.First().Longitude, nodes.Last().Latitude, nodes.Last().Longitude);
             return weight;
        }

        private static void Swap(List<FortData> list, int indexA, int indexB)
        {
            FortData tmp = list[indexA];
            list[indexA] = list[indexB];
            list[indexB] = tmp;
        }

        public static List<FortData> generatePath(List<FortData> nodes)
        {
            Console.WriteLine(getWeight(nodes));
            bool improvement;
            double bestWeight = getWeight(nodes);
            Logger.Write($"Reducing path length from {bestWeight}");
            List<FortData> bestSolutionOverall = nodes;


            do
            {
                improvement = false;
                List<FortData> bestSolutionThisRun = new List<FortData>(bestSolutionOverall);
                for (int i = 0; i < nodes.Count; i++)
                    for (int ii = 0; ii < nodes.Count; ii++)
                    {
                        List<FortData> nodesCopy = new List<FortData>(bestSolutionThisRun);
                        Swap(nodesCopy, i, ii);
                        double newWeight = getWeight(nodesCopy);
                        if (newWeight < bestWeight)
                        {
                            bestWeight = newWeight;
                            bestSolutionThisRun = nodesCopy;
                            improvement = true;
                        }
                    }
                if (improvement)
                {
                    Logger.Write($"New reduced length: {bestWeight}");
                    bestSolutionOverall = bestSolutionThisRun;
                }
            } while (improvement);

            return bestSolutionOverall;
        }
    }
}
