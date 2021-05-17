using System;
using System.Linq;
using System.IO;
using System.Text;
using System.Collections;
using System.Collections.Generic;

class Cell
{
    public int index;
    public int richness;
    public int[] neighbours;

    public Cell(int index, int richness, int[] neighbours)
    {
        this.index = index;
        this.richness = richness;
        this.neighbours = neighbours;
    }
}

class Tree
{
    public int cellIndex;
    public int size;
    public bool isMine;
    public bool isDormant;

    public Tree(int cellIndex, int size, bool isMine, bool isDormant)
    {
        this.cellIndex = cellIndex;
        this.size = size;
        this.isMine = isMine;
        this.isDormant = isDormant;
    }
}

class Action
{
    public const string WAIT = "WAIT";
    public const string SEED = "SEED";
    public const string GROW = "GROW";
    public const string COMPLETE = "COMPLETE";

    public static Action Parse(string action)
    {
        string[] parts = action.Split(' ');
        switch (parts[0])
        {
            case WAIT:
                return new Action(WAIT);
            case SEED:
                return new Action(SEED, int.Parse(parts[1]), int.Parse(parts[2]));
            case GROW:
            case COMPLETE:
            default:
                return new Action(parts[0], int.Parse(parts[1]));
        }
    }

    public string type;
    public int targetCellIdx;
    public int sourceCellIdx;

    public Action(string type, int sourceCellIdx, int targetCellIdx)
    {
        this.type = type;
        this.targetCellIdx = targetCellIdx;
        this.sourceCellIdx = sourceCellIdx;
    }

    public Action(string type, int targetCellIdx)
        : this(type, 0, targetCellIdx)
    {
    }

    public Action(string type)
        : this(type, 0, 0)
    {
    }

    public override string ToString()
    {
        if (type == WAIT)
        {
            return Action.WAIT;
        }
        if (type == SEED)
        {
            return string.Format("{0} {1} {2}", SEED, sourceCellIdx, targetCellIdx);
        }
        return string.Format("{0} {1}", type, targetCellIdx);
    }
}

class Game
{
    public int day;
    public int nutrients;
    public List<Cell> board;
    public List<Action> possibleActions;
    public List<Tree> trees;
    public int mySun, opponentSun;
    public int myScore, opponentScore;
    public bool opponentIsWaiting;

    public IEnumerable<Tree> MyTrees { get { return this.trees.Where(t => t.isMine); } }
    public IEnumerable<Tree> OponentTrees { get { return this.trees.Where(t => !t.isMine); } }

    public Game()
    {
        board = new List<Cell>();
        possibleActions = new List<Action>();
        trees = new List<Tree>();
    }

    public int CalculateCostToGrow(Tree tree)
    {
        switch (tree.size)
        {
            case 0:
                return 1 + this.MyTrees.Where(t => t.size == 1).Count();
            case 1:
                return 3 + this.MyTrees.Where(t => t.size == 2).Count();
            case 2:
                return 7 + this.MyTrees.Where(t => t.size == 3).Count();
            default:
                return 0;
        }
    }

    public int CalculateCostToSeed()
    {
        return this.MyTrees.Where(t => t.size == 0).Count();
    }

    public Action GetNextAction()
    {
        var h0 = new CompleteTreeActionHandler();
        var h1 = new GrowTreeActionHandler();
        var h2 = new EjectSeedActionHandler();
        var h3 = new DefaultActionHandler();
        h0.SetSuccessor(h1);
        h1.SetSuccessor(h2);
        h2.SetSuccessor(h3);

        var finalAction = h0.HandleActions(this);
        return finalAction;
    }
}

class EjectSeedActionHandler : ActionsHandler
{
    public override Action GetBestAction(Game game)
    {
        var optimalCell = this.GetOptimalCell(game);
        if (game.CalculateCostToSeed() <= game.mySun &&
            game.MyTrees.Where(t => t.size == 0).Count() == 0 &&
            optimalCell != null)
        {
            game.trees.Add(new Tree(optimalCell.Item2.index, 0, true, true));
            return Action.Parse(Action.SEED + " " + optimalCell.Item1.index + " " + optimalCell.Item2.index);
        }

        return null;
    }

    public Tuple<Cell, Cell> GetOptimalCell(Game game)
    {
        var cells = new List<Tuple<Cell, Cell>>();
        var trees = game.MyTrees.Where(t => !t.isDormant && t.size > 0);
        foreach (var tree in trees)
        {
            var sourceCell = game.board[tree.cellIndex];
            var neighborCells = this.GetNeigborCells(game, sourceCell, sourceCell);
            cells.AddRange(neighborCells);
            if (tree.size > 1)
            {
                foreach (Cell cell in neighborCells.Select(c => c.Item2))
                {
                    neighborCells = this.GetNeigborCells(game, cell, sourceCell);
                    cells.AddRange(neighborCells);

                    if (tree.size > 2)
                    {
                        foreach (var outerCell in neighborCells.Select(c => c.Item2))
                        {
                            neighborCells = this.GetNeigborCells(game, outerCell, sourceCell);
                            cells.AddRange(neighborCells);
                        }
                    }
                }
            }
        }

        var treeIndexes = game.trees.Select(t => t.cellIndex);
        var myTreeIndexes = game.MyTrees.Select(t => t.cellIndex);
        var availableCells = cells.OrderByDescending(c => c.Item2.richness)
                            .ThenBy(x => x.Item2.index).Where(c => 
        {
            return !treeIndexes.Any(t => t == c.Item2.index) &&
                     c.Item2.richness > 0;
        });

        var distantCells = availableCells.Where(c => !c.Item2.neighbours.Any(nc => myTreeIndexes.Contains(nc)));
        if (distantCells.Any())
        {
            return distantCells.First();
        }

        return null;
    }

    private IEnumerable<Tuple<Cell, Cell>> GetNeigborCells(Game game, Cell cell, Cell sourceCell)
    {
        return cell.neighbours.Where(c => c != -1).Select(c => new Tuple<Cell, Cell>(sourceCell, game.board[c]));
    }
}

class GrowTreeActionHandler : ActionsHandler
{
    public override Action GetBestAction(Game game)
    {
        var treesByRichness = game.MyTrees.Where(t => !t.isDormant)
            .OrderByDescending(t => game.board[t.cellIndex].richness)
            .ThenByDescending(t => t.size);
        var targetTree = treesByRichness.Where(t => game.CalculateCostToGrow(t) <= game.mySun && t.size < 3)
            .OrderByDescending(t => t.size)
            .ThenBy(t => game.CalculateCostToGrow(t))
            .FirstOrDefault();

        if (targetTree != null)
        {
            targetTree.size++;
            return Action.Parse(Action.GROW + " " + targetTree.cellIndex);
        }

        return null;
    }
}

class CompleteTreeActionHandler : ActionsHandler
{
    public override Action GetBestAction(Game game)
    {
        var treesByRichness = game.MyTrees.Where(t => !t.isDormant)
            .OrderByDescending(t => game.board[t.cellIndex].richness);

        var targetTree = treesByRichness.FirstOrDefault(t => t.size == 3);
        var dayToStartCompleting = 23 - game.MyTrees.Where(t => t.size == 3).Count();
        var lowNutrition = game.nutrients < 12;
        var moreTreesThanOpponent = game.MyTrees.Where(t => t.size == 3).Count() > game.OponentTrees.Where(t => t.size == 3).Count();
        if (((game.day > 10 && moreTreesThanOpponent) || game.day >= dayToStartCompleting || lowNutrition) &&
            targetTree != null && game.mySun >= 4)
        {
            game.trees.Remove(targetTree);
            return Action.Parse(Action.COMPLETE + " " + targetTree.cellIndex);
        }

        return null;
    }
}

class DefaultActionHandler : ActionsHandler
{
    public override Action GetBestAction(Game game)
    {
        return Action.Parse(Action.WAIT);
    }
}

abstract class ActionsHandler
{
    protected ActionsHandler successor;

    public void SetSuccessor(ActionsHandler successor)
    {
        this.successor = successor;
    }

    public Action HandleActions(Game game)
    {
        var action = this.GetBestAction(game);
        if (action != null)
        {
            return action;
        }
        else if (successor != null)
        {
            return successor.HandleActions(game);
        }

        return null;
    }

    public abstract Action GetBestAction(Game game);
}

class Player
{
    static void Main(string[] args)
    {
        string[] inputs;

        Game game = new Game();

        int numberOfCells = int.Parse(Console.ReadLine()); // 37
        for (int i = 0; i < numberOfCells; i++)
        {
            inputs = Console.ReadLine().Split(' ');
            int index = int.Parse(inputs[0]); // 0 is the center cell, the next cells spiral outwards
            int richness = int.Parse(inputs[1]); // 0 if the cell is unusable, 1-3 for usable cells
            int neigh0 = int.Parse(inputs[2]); // the index of the neighbouring cell for each direction
            int neigh1 = int.Parse(inputs[3]);
            int neigh2 = int.Parse(inputs[4]);
            int neigh3 = int.Parse(inputs[5]);
            int neigh4 = int.Parse(inputs[6]);
            int neigh5 = int.Parse(inputs[7]);
            int[] neighs = new int[] { neigh0, neigh1, neigh2, neigh3, neigh4, neigh5 };
            Cell cell = new Cell(index, richness, neighs);
            game.board.Add(cell);
        }

        // game loop
        while (true)
        {
            game.day = int.Parse(Console.ReadLine()); // the game lasts 24 days: 0-23
            game.nutrients = int.Parse(Console.ReadLine()); // the base score you gain from the next COMPLETE action
            inputs = Console.ReadLine().Split(' ');
            game.mySun = int.Parse(inputs[0]); // your sun points
            game.myScore = int.Parse(inputs[1]); // your current score
            inputs = Console.ReadLine().Split(' ');
            game.opponentSun = int.Parse(inputs[0]); // opponent's sun points
            game.opponentScore = int.Parse(inputs[1]); // opponent's score
            game.opponentIsWaiting = inputs[2] != "0"; // whether your opponent is asleep until the next day

            game.trees.Clear();
            int numberOfTrees = int.Parse(Console.ReadLine()); // the current amount of trees
            for (int i = 0; i < numberOfTrees; i++)
            {
                inputs = Console.ReadLine().Split(' ');
                int cellIndex = int.Parse(inputs[0]); // location of this tree
                int size = int.Parse(inputs[1]); // size of this tree: 0-3
                bool isMine = inputs[2] != "0"; // 1 if this is your tree
                bool isDormant = inputs[3] != "0"; // 1 if this tree is dormant
                Tree tree = new Tree(cellIndex, size, isMine, isDormant);
                game.trees.Add(tree);
            }

            game.possibleActions.Clear();
            int numberOfPossibleMoves = int.Parse(Console.ReadLine());
            for (int i = 0; i < numberOfPossibleMoves; i++)
            {
                string possibleMove = Console.ReadLine();
                game.possibleActions.Add(Action.Parse(possibleMove));
            }

            Action action = game.GetNextAction();
            Console.WriteLine(action);
        }
    }
}