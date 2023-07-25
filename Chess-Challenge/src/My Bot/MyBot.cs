﻿using System;
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

    private const int
        AUTO_ACCEPT_WEIGHT =
            10000; // Stop calculating a obviously winning branch (currently a hack to get it to actually checkmate lol)

    private const int DRAW_WEIGHT = -1000; // Draws are dumb, try to avoid them

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

        return GetRandomBestMove(Analyze(board, MINIMUM_DEPTH, true, 0, timer));
    }

    public int GetMaxDepth(Timer timer)
    {
        if (timer.MillisecondsRemaining > 40000)
        {
            return MAXIMUM_DEPTH;
        }
        else if (timer.MillisecondsRemaining > 20000)
        {
            return MAXIMUM_DEPTH - 2;
        }
        else if (timer.MillisecondsRemaining > 10000)
        {
            return MAXIMUM_DEPTH - 4; // only 5 moves deep at this point, it won't calculate much but time is short!
        }
        else
        {
            return
                MAXIMUM_DEPTH -
                6; // Practically nothing but it has less than 10 seconds left and HAS to make moves or it loses
        }
    }

    // Extend search for things like captures
    public int ExtendDepth(Board board, Move move, int depth, int totalDepth, Timer timer)
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

        return Math.Min(GetMaxDepth(timer), totalDepth + returnDepth) - totalDepth;
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

    public List<MoveWeight> Analyze(Board board, int depth, bool myTurn, int totalDepth, Timer timer)
    {
        var moveWeights = GetCachedMoveWeights(board);

        if (depth < 1) // Allow for negatives, normally wouldn't happen but our extension method might limit us as the scheduler forces shallower calculation due to time pressure
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

            var extendedDepth = ExtendDepth(board, moveWeight.Move, depth, totalDepth, timer);
            board.MakeMove(moveWeight.Move);

            if (board.IsInCheckmate()) // A bit hacky - don't calculate further if it's checkmate
            {
                board.UndoMove(moveWeight.Move);
                return moveWeight;
            }

            var output = Analyze(board, extendedDepth - 1, !myTurn, totalDepth + 1, timer);

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

        var startFile = move.StartSquare.File;
        var endFile = move.TargetSquare.File;

        if (move.IsCastles == false &&
            ((move.MovePieceType == PieceType.King && (board.HasKingsideCastleRight(board.IsWhiteToMove) ||
                                                       board.HasQueensideCastleRight(board.IsWhiteToMove))) ||
             (move.MovePieceType == PieceType.Rook &&
              ((startFile == 0 && board.HasQueensideCastleRight(board.IsWhiteToMove)) ||
               (startFile == 7 && board.HasKingsideCastleRight(board.IsWhiteToMove))))))
        {
            weight += NO_MORE_CASTLE_WEIGHT;
        }

        // Simple check for "developing" a piece, encouraging pieces to move off the back rank
        // Also, ignore the king and queen to discourage moving them in the early game
        if (move.MovePieceType != PieceType.King &&
            move.MovePieceType != PieceType.Queen &&
            (board.IsWhiteToMove && startFile == 0 && endFile != 0) ||
            (!board.IsWhiteToMove && startFile == 7 && endFile != 7))
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

        // This should also calculate based upon material left on the board.  I want a draw if I'm down material, and I want to avoid it otherwise
        if (board.IsDraw())
        {
            weight += DRAW_WEIGHT;
        }

        board.UndoMove(move);

        return weight;
    }
}