using System;
using System.Collections.Generic;
using System.Linq;
using TileData;
using UnityEngine;
using UnityEngine.Tilemaps;
using Utils;

namespace Snake.Strategy
{
public class AStarStrategy : MoveStrategy
{
    private Tilemap _tilemap;
    private TileBase _emptyTile;
    private TileBase _pathTile;

    private MapManager _mapManager;

    private BoundsInt _bounds;
    private Queue<SnakePart> _path;

    private bool _noSeparateSpaces;
    private bool _morePaths;

    public AStarStrategy(BoundsInt bounds, bool noSeparateSpaces, bool morePaths)
    {
        _tilemap = GameObject.Find("Grid/Indicators").GetComponent<Tilemap>();
        _pathTile = Resources.Load<TileBase>("Tiles/Path");
        _mapManager = UnityEngine.Object.FindObjectOfType<MapManager>();
        
        _bounds = bounds;
        _path = new Queue<SnakePart>();

        _noSeparateSpaces = noSeparateSpaces;
        _morePaths = morePaths;
    }

    public Vector3Int GetDirection(SnakeParts parts, Vector3Int target)
    {
        // if path exists return first step
        if (_path.Count > 0)
        {
            PaintPath(2);
            return _path.Dequeue().Direction;
        }
        
        long start = DateTimeOffset.Now.ToUnixTimeMilliseconds();

        // else compute new path
        var options = new PriorityQueue<Tuple<SnakeParts, SnakeParts>, int>();
        int distance = Util.ManhattanDistance(parts.Last.Value.Pos, target);
        options.Enqueue(new Tuple<SnakeParts, SnakeParts>(parts, new SnakeParts()), distance);
        
        var visited = new HashSet<Vector3Int>();

        while (options.Count > 0)
        {
            (SnakeParts curParts, SnakeParts curPath) = options.Dequeue();
            Vector3Int curHead = curParts.Last.Value.Pos;
            visited.Add(curHead);
            
            if (curHead.Equals(target))
            {
                long duration = DateTimeOffset.Now.ToUnixTimeMilliseconds() - start;
                Debug.Log($"Decision took: {duration}");
                
                curPath.ToList().ForEach(_path.Enqueue);
                PaintPath(2);
                return _path.Dequeue().Direction;
            }

            void AddOption(Vector3Int possibleDir)
            {
                SnakeParts newParts = curParts.CloneAndMove(possibleDir);
                SnakeParts newPath = curPath.Clone();
                newPath.AddLast(new SnakePart { Pos = curHead, Direction = possibleDir });
                int newDistance = newPath.Count + Util.ManhattanDistance(curHead + possibleDir, target);
                
                options.Enqueue(new Tuple<SnakeParts, SnakeParts>(newParts, newPath), newDistance);
            }
            
            LinkedList<Vector3Int> possibleDirs = Util.GetPossibleDirections(curParts, _bounds, curHead);

            var chosenDirs = new LinkedList<Vector3Int>();

            foreach (Vector3Int possibleDir in possibleDirs)
            {
                if (_noSeparateSpaces)
                {
                    SnakeParts possibleParts = curParts.CloneAndMove(possibleDir);
                    Vector3Int possibleHead = curHead + possibleDir;

                    Dictionary<Vector3Int, bool> grid = Util.GetGrid(possibleParts, _bounds);

                    var adjacentPossiblePositions = new LinkedList<Vector3Int>();
                    foreach (Vector3Int adjacentPossibleDir in Util.GetPossibleDirections(grid, possibleHead))
                    {
                        adjacentPossiblePositions.AddLast(possibleHead + adjacentPossibleDir);
                    }

                    if (adjacentPossiblePositions.Count == 1)
                    {
                        chosenDirs.Clear();
                        chosenDirs.AddLast(possibleDir);
                        break;
                    }
                    
                    if (visited.Contains(possibleHead))
                        continue;

                    if (adjacentPossiblePositions.Count == 2)
                    {
                        bool sameAxis = adjacentPossiblePositions.First.Value.x == adjacentPossiblePositions.Last.Value.x
                                        || adjacentPossiblePositions.First.Value.y == adjacentPossiblePositions.Last.Value.y;
                        TileDataHolder tileData = _mapManager.GetTileData(possibleHead + possibleDir);

                        if (sameAxis && !tileData.wall)
                            continue;

                        if (sameAxis && tileData.wall)
                        {
                            Vector3Int a = adjacentPossiblePositions.First.Value;
                            Vector3Int b = adjacentPossiblePositions.Last.Value;
                            var searchA = new AStarSearch(_bounds, possibleParts, b, a);
                            var searchB = new AStarSearch(_bounds, possibleParts, a, b);

                            var connected = false;

                            while (!connected)
                            {
                                searchA.VisitNext();
                                searchB.VisitNext();

                                if (searchA.Found() || searchB.Found())
                                    connected = true;

                                if (!searchA.CanVisitNext() || !searchB.CanVisitNext())
                                    break;
                            }

                            if (!connected)
                            {
                                chosenDirs.Clear();

                                if (!searchA.CanVisitNext())
                                    chosenDirs.AddLast(b - possibleHead);

                                if (!searchB.CanVisitNext())
                                    chosenDirs.AddLast(a - possibleHead);

                                break;
                            }
                        }

                        if (!sameAxis)
                        {
                            Vector3Int corner = Util.GetFourthSquare(possibleHead, 
                                adjacentPossiblePositions.First.Value,
                                adjacentPossiblePositions.Last.Value);

                            if (grid[corner])
                            {
                                Vector3Int ahead = possibleHead + possibleParts.Last.Value.Direction;
                                adjacentPossiblePositions.Remove(ahead);
                                
                                chosenDirs.Clear();
                                chosenDirs.AddLast(adjacentPossiblePositions.First.Value - possibleHead);
                                break;
                            }
                        }
                    }

                    if (adjacentPossiblePositions.Count == 3)
                    {
                        Vector3Int ahead = possibleHead + possibleParts.Last.Value.Direction;
                        adjacentPossiblePositions.Remove(ahead);

                        Vector3Int a = adjacentPossiblePositions.First.Value;
                        Vector3Int b = adjacentPossiblePositions.Last.Value;
                        
                        Vector3Int cornerA = Util.GetFourthSquare(possibleHead, ahead, a);
                        Vector3Int cornerB = Util.GetFourthSquare(possibleHead, ahead, b);

                        if (grid[cornerA] || grid[cornerB])
                        {
                            chosenDirs.Clear();
                            if (grid[cornerA])
                            {
                                chosenDirs.AddLast(a - possibleHead);
                                break;
                            }
                            if (grid[cornerB])
                            {
                                chosenDirs.AddLast(b - possibleHead);
                                break;
                            }
                        }
                    }
                }
                else
                {
                    if (visited.Contains(curHead + possibleDir))
                        continue;
                }

                chosenDirs.AddLast(possibleDir);
            }
            
            chosenDirs.ToList().ForEach(AddOption);
        }

        Debug.Log("Could not find path");
        return Vector3Int.zero;
    }

    private void PaintPath(int start)
    {
        for (int x = _bounds.xMin; x < _bounds.xMax; x++)
        {
            for (int y = _bounds.yMin; y < _bounds.yMax; y++)
            {
                var pos = new Vector3Int(x, y);
                _tilemap.SetTile(pos, null);
            }
        }

        SnakePart[] parts = _path.ToArray();
        for (int i = start; i < parts.Length; i++)
        {
            _tilemap.SetTile(new TileChangeData(
                parts[i].Pos, 
                _pathTile,
                Color.white,
                Matrix4x4.Rotate(Util.DirectionToQuaternion(parts[i].Direction))
            ), true);

        }
    }
}
}