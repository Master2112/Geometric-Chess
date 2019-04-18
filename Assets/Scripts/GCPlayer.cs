using MachineLearning;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

public enum PlayerType {
		P1, P2
}

public class GCPlayer : IClicker, IInputReceiver {

    private Grid grid;
	private PlayerType type;

	private List<Piece> pieces;
	private List<Piece> eatenPieces;

	private Piece piece;
	private Piece checkedBy; //Experimental

    private bool myTurn = false;
    private bool didTurn = false;

    private static StateAgent brain1, brain2;

    public StateAgent brain
    {
        get
        {
            return type == PlayerType.P1 ? brain1 : brain2;
        }

        set
        {
            if (type == PlayerType.P1)
                brain1 = value;
            else
                brain2 = value;
        }
    }

	//Experimental
	public bool IsChecked {
		get {return checkedBy != null;}
	}

	public List<Piece> Pieces {
		get {return pieces;}
	}

	public List<Piece> EatenPieces {
		get {return eatenPieces;}
	}

	//Experimental
	public Piece CheckedBy {
		get {return checkedBy;}
		set {
			checkedBy = value;
		}
	}

	public Piece HoldingPiece {
		get {return piece;}
	}

	public bool IsReady {
		get {
			for (int i = 0; i < pieces.Count; i++) {
				if (!pieces[i].IsReady) return false;
			}

			return true;
		}
	}

	public PlayerType Type {
		get {return type;}
	}

	public GCPlayer(PlayerType type) {
		this.type = type;
		pieces = new List<Piece>();
		eatenPieces = new List<Piece>();
	}

	public void EnableInput() {
		InputManager.InputEvent += OnInputEvent;
        myTurn = true;
        didTurn = false;
	}

	public void DisableInput() {
		InputManager.InputEvent -= OnInputEvent;
        myTurn = false;
	}

	void OnDisable() {
		DisableInput();
	}

    State GetBoardState()
    {
        List<StateAction> actions = new List<StateAction>();

        for (int i = 0; i < pieces.Count; i++)
        {
            pieces[i].team = (int)type;
            pieces[i].Compute();

            for (int m = 0; m < pieces[i].PossibleMoves.Count; m++)
            {
                //if (!Rules.IsCheckMove(this, pieces[i], pieces[i].PossibleMoves[m], true) && pieces[i].Node != pieces[i].PossibleMoves[m])
                {
                    string moveString = pieces[i].Node.col + "-" + pieces[i].Node.row + "to" + pieces[i].PossibleMoves[m].col + "-" + pieces[i].PossibleMoves[m].row;
                    actions.Add(new StateAction(moveString, 100000));
                }
            }

            for (int m = 0; m < pieces[i].PossibleEats.Count; m++)
            {
                //if (!Rules.IsCheckEat(this, pieces[i], pieces[i].PossibleEats[m], true) && pieces[i].Node != pieces[i].PossibleEats[m])
                {
                    int fc = pieces[i].Node.col;
                    int fr = pieces[i].Node.row;

                    int tc = pieces[i].PossibleEats[m].col;
                    int tr = pieces[i].PossibleEats[m].row;

                    string moveString = fc + "-" + fr + "to" + tc + "-" + tr;
                    actions.Add(new StateAction(moveString, 100000));
                }
            }
        }

        Piece.AllPieces = Piece.AllPieces.OrderBy(o => o.PieceStateString).ToList();

        string stateStr = "";

        for (int i = 0; i < Piece.AllPieces.Count; i++)
        {
            stateStr += Piece.AllPieces[i].PieceStateString;
        }

        return new State(stateStr, actions.ToArray());
    }

    public void UpdateAI()
    {
        if (grid == null)
            grid = GameObject.FindObjectOfType<Grid>();

        if (myTurn && !didTurn)
        {
            didTurn = true;

            State boardState = GetBoardState();

            Debug.Log("State visited counter: " + boardState.TimesVisited);

            if (brain == null)
            {
                brain = new StateAgent(boardState);
            }
            else
            {
                brain.SetState(boardState);
            }

            //get action from brain, execute.

            StateAction action = brain.GetChosenActionForCurrentState();

            string[] moves = Regex.Split(action.ActionString, "to");
            string[] from = Regex.Split(moves[0], "-");
            string[] to = Regex.Split(moves[1], "-");

            Debug.Log(action.ActionString + ", Quality: " + action.GetDeepEvaluation() + " (" + action.ActionEvaluation + ") --- " + brain.LearnedStates + "///" + brain.EvaluatedActions);

            if(action.GetDeepEvaluation() != action.ActionEvaluation)
            {
                Debug.Log("///////////////////////////////////////////////////////////////");
            }

            foreach (Node n in grid.grid)
            {
                n.UnhighlightEat();
                n.UnhighlightMove();
            }

            Node fromNode = grid.GetNodeAt(int.Parse(from[1]), int.Parse(from[0]));
            Node toNode = grid.GetNodeAt(int.Parse(to[1]), int.Parse(to[0]));

            fromNode.HighlightMove();
            toNode.HighlightEat();

            piece = fromNode.Piece;
            piece.Pickup();
            GameManager.Instance.GameState.Grab();
            int reward = 0;

            Piece tPiece = toNode.Piece;
            if (tPiece == null)
            {
                if (piece.IsPossibleMove(toNode))
                {
                    if (Rules.IsCheckMove(this, piece, toNode, true))
                    {
                        Debug.Log("Move checked, not allowed"); // do nothing

                        brain.EvaluateLastAction(-10000);
                        GameManager.Instance.GameState.Checkmate();
                        GameManager.Instance.GameOver(GameManager.Instance.PlayerOponent, GameOverType.CHECKMATE);
                    }
                    else
                    {
                        piece.MoveToXZ(toNode, Drop);
                        GameManager.Instance.GameState.Place();
                    }
                }
            }
            else
            {
                if (piece.IsPossibleEat(toNode))
                {
                    if (Rules.IsCheckEat(this, piece, toNode, true))
                    {
                        Debug.Log("Eat checked"); // do nothing

                        brain.EvaluateLastAction(-10000);
                        GameManager.Instance.GameState.Checkmate();
                        GameManager.Instance.GameOver(GameManager.Instance.PlayerOponent, GameOverType.CHECKMATE);
                    }
                    else
                    {
                        GCPlayer oppPlayer = GameManager.Instance.Opponent(this);

                        oppPlayer.brain.EvaluateLastAction(-tPiece.GetPieceValue());
                        reward = tPiece.GetPieceValue();

                        oppPlayer.RemovePiece(tPiece);
                        AddEatenPieces(tPiece);
                        tPiece.ScaleOut(0.2f, 1.5f);
                        piece.MoveToXZ(toNode, Drop);
                        GameManager.Instance.GameState.Place();
                    }
                }
            }

            State newState = GetBoardState();
            
            brain.PerformStateAction(action, newState);
            brain.EvaluateLastAction(reward);
        }
    }

	public void OnInputEvent(InputActionType action) {
        return; //Disabled because AI
		switch (action) {
			case InputActionType.GRAB_PIECE:
				Node gNode = Finder.RayHitFromScreen<Node>(Input.mousePosition);
				if (gNode == null) break;
				piece = gNode.Piece;
				if (piece == null) break;
				if (!piece.IsReady) break;
				if (Click(gNode) && piece && Has(piece) && Click(piece)) {
					piece.Pickup();
					piece.Compute();
					piece.HighlightPossibleMoves();
					piece.HighlightPossibleEats();
					GameManager.Instance.GameState.Grab();
				} 

				//check clickable for tile and piece then pass Player
				//check if player has piece - PIECE 
				//check if player has piece if not empty - NODE 
				break;
			case InputActionType.CANCEL_PIECE:
					if (piece != null) {
						//if (!piece.IsReady) break;
						piece.Drop();
						piece = null;
						GameManager.Instance.GameState.Cancel();
					}
				break;
			case InputActionType.PLACE_PIECE:
				Node tNode = Finder.RayHitFromScreen<Node>(Input.mousePosition);
				if (tNode == null) break;
				Piece tPiece = tNode.Piece;
				if (tPiece == null) {
					if (piece.IsPossibleMove(tNode)) {
						if (Rules.IsCheckMove(this,piece,tNode, true)) {
							Debug.Log("Move checked"); // do nothing
						} else {
							piece.MoveToXZ(tNode, Drop);
							GameManager.Instance.GameState.Place();
						}
					}
				} else {
					if (piece.IsPossibleEat(tNode)) {
						if (Rules.IsCheckEat(this,piece,tNode, true)) {
							Debug.Log("Eat checked"); // do nothing
						} else {
							GCPlayer oppPlayer = GameManager.Instance.Opponent(this);
							oppPlayer.RemovePiece(tPiece);
							AddEatenPieces(tPiece);
							tPiece.ScaleOut(0.2f, 1.5f);
							piece.MoveToXZ(tNode, Drop);
							GameManager.Instance.GameState.Place();
						}
					}
				}
				break;
		}
	}

	public void ClearPiecesPossibles() {
		for (int i = 0; i < pieces.Count; i++) {
			pieces[i].ClearPossibleEats();
			pieces[i].ClearPossibleMoves();
		}
	}

	public void ClearCheck() {
		if (checkedBy == null) return;
		checkedBy = null;
		//checkedBy.ClearCheck(this);
	}

	//the methods inside must be in order
	private void Drop() {
		piece.Drop();
		piece.Compute();
		GameManager.Instance.GameState.Release();
		piece = null;
	}

	public bool Has(Piece piece) {
		return pieces.Contains(piece);
	}

	public bool Click(IClickable clickable) {
		if (clickable == null) return false;
		return clickable.Inform<GCPlayer>(this); 
	}

	public void AddPieces(params Piece[] pieces) {
		for (int i = 0; i < pieces.Length; i++) {
			this.pieces.Add(pieces[i]);
		}
	}

	public void AddEatenPieces(params Piece[] pieces) {
		for (int i = 0; i < pieces.Length; i++) {
			this.eatenPieces.Add(pieces[i]);
		}
	}

	public bool RemovePiece(Piece piece) {
        piece.Unregister();
		return pieces.Remove(piece);
	}

	public void ComputePieces() {
		for (int i = 0; i < pieces.Count; i++) {
			pieces[i].Compute();
		}
	}
}
