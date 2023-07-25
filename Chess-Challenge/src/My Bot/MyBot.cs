using System;
using System.Collections.Generic;
using System.Linq;
using ChessChallenge.API;

public class MyBot : IChessBot
{
    // Piece values: null, pawn, knight, bishop, rook, queen, king
    int[] pieceValues = { 0, 100, 300, 300, 500, 900, 10000 };


    private const int PAWN_ADVANCE_WEIGHT = 1; // Reward advancing pawn (by square?)
    private const int CHECK_WEIGHT = 10; // Reward checks
    private const int CHECKMATE_WEIGHT = 100000; // Highly reward checkmate
    private const int ROOK_TO_OPEN_FILE_WEIGHT = 2; // Reward moving a rook to an open file
    private const int NO_MORE_CASTLE_WEIGHT = -5; // Punish moving the king without castling
    private const int CAPTURE_WEIGHT = -1; // *slightly* punish captures, to encourage letting the enemy capture first
    private const int DEVELOP_WEIGHT = 5; // Reward developing a piece (moving it for the first time
    private const int CASTLE_WEIGHT = 5; // Reward for castling
    private const int MINIMUM_DEPTH = 2; // Search every permutation at least this deep 
    private const int MAXIMUM_DEPTH = 9; // Never go farther than this deep


    private const int PRUNE_WEIGHT = -1000; // Stop calculating bad branches
    private const int AUTO_ACCEPT_WEIGHT = 10000; // Stop calculating a obviously winning branch (currently a hack to get it to actually checkmate lol)

    private readonly IDictionary<ulong, List<MoveWeight>> _transpositionTable =
        new Dictionary<ulong, List<MoveWeight>>();

    public readonly Random random;

    public class MoveWeight
    {
        public Move Move { get; set; }
        public int Weight { get; set; }

        public override string ToString()
        {
            return $"({Move.ToString()} Weight: {Weight})";
        }
    }

    public MyBot()
    {
        random = new Random();
    }

    public Move Think(Board board, Timer timer)
    {
        if (board.PlyCount == 0)
        {
            return new Move("e2e4", board); //start strong!
        }

        return GetRandomBestMove(Analyze(board, MINIMUM_DEPTH, true, 0));
    }

    // Extend search for things like captures
    public int ExtendDepth(Board board, Move move, int depth, int totalDepth)
    {
        if (depth > 1)
        {
            return depth;
        }
        var returnDepth = depth;
        if (move.IsCapture)
        {
            returnDepth += 1;
        }
        else
        {
            board.MakeMove(move);

            if (board.IsInCheck())
            {
                returnDepth += 1;
            }

            board.UndoMove(move);
        }

        return Math.Min(MAXIMUM_DEPTH, totalDepth + returnDepth) - totalDepth;
    }

    public List<MoveWeight> GetCachedMoveWeights(Board board)
    {
        if (_transpositionTable.TryGetValue(board.ZobristKey, out var weights))
        {
            return weights;
        }

        var generated = board.GetLegalMoves().Select((move) => new MoveWeight()
        {
            Move = move,
            Weight = GetWeight(board, move),
        }).ToList();

        generated.Sort((x, y) => y.Weight.CompareTo(x.Weight));

        _transpositionTable.Add(board.ZobristKey, generated);

        return generated;
    }

    public List<MoveWeight> Analyze(Board board, int depth, bool myTurn, int totalDepth)
    {
        var moveWeights = GetCachedMoveWeights(board);

        if (depth == 0)
        {
            return moveWeights;
        }

        if (moveWeights.Count == 0) //TODO: How the heck is this happening?
        {
            return moveWeights;
        }
        var highestWeight = moveWeights[0].Weight;
        
        if (highestWeight > AUTO_ACCEPT_WEIGHT)
        {
            return moveWeights; // Stop calculating if we have a mate, this is so hacky
        }

        var output = moveWeights.Select(moveWeight =>
        {
            if (moveWeight.Weight < PRUNE_WEIGHT) // This pruning function literally never happens
            {
                Console.WriteLine("Pruned " + moveWeight.Move);
                return moveWeight;
            }
            var extendedDepth = ExtendDepth(board, moveWeight.Move, depth, totalDepth);
            board.MakeMove(moveWeight.Move);
            
            if (board.IsInCheckmate()) // A bit hacky - don't calculate further if it's checkmate
            {
                board.UndoMove(moveWeight.Move);
                return moveWeight;
            }
            
            var output = Analyze(board, extendedDepth - 1, !myTurn, totalDepth + 1);

            var highestWeight = GetHighestWeight(output);

            board.UndoMove(moveWeight.Move);

            return new MoveWeight()
            {
                Move = moveWeight.Move,
                Weight = moveWeight.Weight - highestWeight
            };
        }).ToList();

        return output;
    }

    public int GetHighestWeight(List<MoveWeight> moveWeights)
    {
        return moveWeights.Aggregate(
            int.MinValue, (cur, next) =>
            {
                if (next.Weight > cur)
                {
                    return next.Weight;
                }

                return cur;
            });
    }

    public Move GetRandomBestMove(List<MoveWeight> moveWeights)
    {
        Console.WriteLine("Candidate moves: " + String.Join(", ", moveWeights));
        return moveWeights.Aggregate(
            (bestWeight: int.MinValue, bestMoves: new List<Move>()), (cur, next) =>
            {
                if (next.Weight > cur.bestWeight)
                {
                    return (next.Weight, new List<Move> { next.Move });
                }

                if (next.Weight == cur.bestWeight)
                {
                    var newBestMoves = new List<Move>(cur.bestMoves);
                    newBestMoves.Add(next.Move);
                    return (cur.bestWeight, newBestMoves);
                }

                return cur;
            }, best =>
            {
                var index = random.Next(best.bestMoves.Count);

                var move = best.bestMoves[index];

                Console.WriteLine(
                    $"Making {move.ToString()} Weight: {best.bestWeight}");
                return move;
            });
    }

    public int GetWeight(Board board, Move move)
    {
        var weight = 0;
        weight += pieceValues[(int)board.GetPiece(move.TargetSquare).PieceType]; // Reward capture

        if (move.MovePieceType == PieceType.Pawn)
        {
            weight += Math.Abs(move.StartSquare.Rank - move.TargetSquare.Rank) * PAWN_ADVANCE_WEIGHT;

            if (move.IsPromotion)
            {
                weight += pieceValues[(int)move.PromotionPieceType] -
                          pieceValues[(int)move.MovePieceType]; // Reward promotion
            }
        }

        if (move.IsCastles)
        {
            weight += CASTLE_WEIGHT;
        }

        if (move.IsCapture)
        {
            weight += CAPTURE_WEIGHT;
        }

        if (move.IsCastles == false &&
            ((move.MovePieceType == PieceType.King && (board.HasKingsideCastleRight(board.IsWhiteToMove) ||
                                                       board.HasQueensideCastleRight(board.IsWhiteToMove))) ||
             (move.MovePieceType == PieceType.Rook &&
              ((move.StartSquare.File == 0 && board.HasQueensideCastleRight(board.IsWhiteToMove)) ||
               (move.StartSquare.File == 7 && board.HasKingsideCastleRight(board.IsWhiteToMove))))))
        {
            weight += NO_MORE_CASTLE_WEIGHT;
        }

        if (move.MovePieceType != PieceType.Pawn && move.MovePieceType != PieceType.King &&
            IsStartPosition(move.StartSquare, move.MovePieceType, board.IsWhiteToMove))
        {
            weight += DEVELOP_WEIGHT;
        }

        board.MakeMove(move);
        if (board.IsInCheck())
        {
            weight += CHECK_WEIGHT;
        }
        
        if (board.IsInCheckmate())
        {
            weight += CHECKMATE_WEIGHT;
        }
        
        board.UndoMove(move);

        return weight;
    }

    private bool IsStartPosition(Square square, PieceType piece, bool isWhite)
    {
        if (isWhite)
        {
            if (piece == PieceType.Pawn && square.Rank != 1) return false;
            if (piece != PieceType.Pawn && square.Rank != 0) return false;
        }
        else
        {
            if (piece == PieceType.Pawn && square.Rank != 6) return false;
            if (piece != PieceType.Pawn && square.Rank != 7) return false;
        }

        return (piece == PieceType.Rook && square.File is 0 or 7) ||
               (piece == PieceType.Knight && square.File is 1 or 6) ||
               (piece == PieceType.Bishop && square.File is 2 or 5) ||
               (piece == PieceType.King && square.File == 4) ||
               (piece == PieceType.Queen && square.File == 3);
    }
}