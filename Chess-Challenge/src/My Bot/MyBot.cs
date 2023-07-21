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

    public Random random;

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

        return GetRandomBestMove(Test(board, 4, true));
    }

    public IEnumerable<(Move move, int weight)> Test(Board board, int depth, bool myTurn)
    {
        var moveWeights = board.GetLegalMoves().Select((move) => (move, weight: GetWeight(board, move)));

        if (depth == 0)
        {
            return moveWeights;
        }

        return moveWeights.Select(moveWeight =>
        {
            board.MakeMove(moveWeight.move);
            var output = Test(board, depth - 1, !myTurn);

            var highestWeight = GetHighestWeight(output) * (myTurn ? 1 : -1);

            board.UndoMove(moveWeight.move);

            return (moveWeight.move, moveWeight.weight + highestWeight);
        });
    }

    public int GetHighestWeight(IEnumerable<(Move move, int weight)> moveWeights)
    {
        return moveWeights.Aggregate(
            int.MinValue, (cur, next) =>
            {
                if (next.weight > cur)
                {
                    return next.weight;
                }

                return cur;
            });
    }

    public Move GetRandomBestMove(IEnumerable<(Move move, int weight)> moveWeights)
    {
        return moveWeights.Aggregate(
            (bestWeight: int.MinValue, bestMoves: new List<Move>()), (cur, next) =>
            {
                if (next.weight > cur.bestWeight)
                {
                    return (next.weight, new List<Move> { next.move });
                }

                if (next.weight == cur.bestWeight)
                {
                    var newBestMoves = new List<Move>(cur.bestMoves);
                    newBestMoves.Add(next.move);
                    return (cur.bestWeight, newBestMoves);
                }

                return cur;
            }, best =>
            {
                var index = random.Next(best.bestMoves.Count);

                var move = best.bestMoves[index];

                Console.WriteLine(
                    $"Making {move.ToString()} Weight: {best.bestWeight} Other moves with equal weight: {best.bestMoves.Count - 1} which are {String.Join(", ", best.bestMoves.ToArray())}");
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