using System;
using UnityEngine;

namespace EmpireBattle
{
    /// <summary>Minimum total squared distance assignment, with reusable Hungarian solver storage.</summary>
    public sealed class FormationAssignment
    {
        readonly float[] costs;
        readonly double[] rowPotential, columnPotential, minimum;
        readonly int[] occupants, previous;
        readonly bool[] visited;
        readonly int stride;
        public FormationAssignment(int capacity)
        {
            stride = capacity;
            costs = new float[checked(capacity * capacity)];
            rowPotential = new double[capacity + 1]; columnPotential = new double[capacity + 1];
            minimum = new double[capacity + 1]; occupants = new int[capacity + 1];
            previous = new int[capacity + 1]; visited = new bool[capacity + 1];
        }
        public void Assign(Vector2[] positions, int[] soldiers, Vector2[] targets, int count, int[] result)
        {
            Array.Clear(rowPotential, 0, count + 1); Array.Clear(columnPotential, 0, count + 1);
            Array.Clear(occupants, 0, count + 1);
            for (int row = 0; row < count; row++)
            for (int column = 0; column < count; column++)
                costs[row * stride + column] = (positions[soldiers[row]] - targets[column]).sqrMagnitude;
            for (int row = 1; row <= count; row++)
            {
                occupants[0] = row;
                int column = 0;
                Array.Clear(visited, 0, count + 1);
                for (int j = 1; j <= count; j++) minimum[j] = double.PositiveInfinity;
                do
                {
                    visited[column] = true;
                    int currentRow = occupants[column], next = 0;
                    double delta = double.PositiveInfinity;
                    for (int j = 1; j <= count; j++)
                    {
                        if (visited[j]) continue;
                        double cost = costs[(currentRow - 1) * stride + j - 1] - rowPotential[currentRow] - columnPotential[j];
                        if (cost < minimum[j]) { minimum[j] = cost; previous[j] = column; }
                        if (minimum[j] < delta) { delta = minimum[j]; next = j; }
                    }
                    for (int j = 0; j <= count; j++)
                        if (visited[j]) { rowPotential[occupants[j]] += delta; columnPotential[j] -= delta; }
                        else minimum[j] -= delta;
                    column = next;
                } while (occupants[column] != 0);
                do
                {
                    int from = previous[column]; occupants[column] = occupants[from]; column = from;
                } while (column != 0);
            }
            for (int column = 1; column <= count; column++) result[column - 1] = soldiers[occupants[column] - 1];
        }
    }
}
