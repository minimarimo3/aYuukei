parser grammar DaihonParser;

options { tokenVocab = DaihonLexer; }

// ============================================================
// トップレベル
// ============================================================

file : eventDecl EOF ;

// ============================================================
// イベント宣言（§2.1）
// ============================================================

eventDecl
    : EVENT_HEADER HEADER_NAME NEWLINE+
      preconditionBlock?
      defaultsBlock?
      scene+
    ;

// ============================================================
// 前提条件ブロック（§2.2）
// ============================================================

preconditionBlock
    : ZENTEI_JOUKEN COLON NEWLINE+
      conditionalJump+
    ;

// 前提条件内で許可される構文：条件付き →おわり のみ
conditionalJump
    : CONDITION_MARKER LPAREN condExpr RPAREN ARROW jumpTarget NEWLINE+
    ;

// ============================================================
// 初期値ブロック（§2.3）
// ============================================================

defaultsBlock
    : SHOKI_CHI COLON NEWLINE+
      (assignment NEWLINE+)+
    ;

// ============================================================
// シーン（§2.5, §2.6）
// ============================================================

scene
    : SCENE_HEADER HEADER_NAME NEWLINE+
      signalDecl?
      conditionDecl?
      stmtList
    ;

// 合図宣言（§2.6.1）
signalDecl
    : AIZU COLON systemEventList NEWLINE+
    ;

systemEventList
    : systemEvent (MATAWA systemEvent)*
    ;

// システムイベント: ＠識別子（.識別子）*
systemEvent
    : AT_SIGN IDENTIFIER (DOT IDENTIFIER)*
    ;

// 条件宣言（§2.6.2）
conditionDecl
    : JOUKEN COLON LPAREN condExpr RPAREN NEWLINE+
    ;

// ============================================================
// 文（§6.1）
// ============================================================

stmtList
    : (stmt NEWLINE+)*
    ;

stmt
    : displaySequence    // 表示列（セリフ・関数呼び出しの連続）
    | assignment         // 代入
    | jump               // ジャンプ
    | conditionalStmt    // 条件付き文
    ;

// 表示列：セリフと関数呼び出しを1行に複数記述可（§6.1）
displaySequence
    : displayElement+
    ;

displayElement
    : dialogue
    | funcCall
    ;

// ============================================================
// 代入（§3）
// ============================================================

// atom を左辺に使用することで、2月14日-style の数字始まり変数名に対応
assignment
    : atom ASSIGN_EQ expr
    ;

// ============================================================
// ジャンプ（§7）
// ============================================================

jump
    : ARROW jumpTarget
    ;

// ジャンプ先：おわり / シーンおわり / シーン名（スペースを含む名前に対応）
jumpTarget
    : OWARI
    | SCENE_OWARI
    | jumpName+
    ;

// シーン名に使用できるトークン（数字始まり名にも対応）
jumpName
    : IDENTIFIER
    | NUMBER
    | HAI
    | IIE
    ;

// ============================================================
// 条件付き文（§6.1, §6.2, §6.3）
// ============================================================

conditionalStmt
    // ブロック記法（§6.2, §6.3）
    : CONDITION_MARKER LPAREN condExpr RPAREN NARA? COLON NEWLINE+
      stmtList
      elseIfBlock*
      elseBlock?
      OWARI
    // 1行記法（§6.1）
    | CONDITION_MARKER LPAREN condExpr RPAREN NARA? singleAction
    ;

// ※（条件）: ブロック — else if（§6.3）
elseIfBlock
    : CONDITION_MARKER ARUIHA LPAREN condExpr RPAREN NARA? COLON NEWLINE+
      stmtList
    ;

// ※それ以外: ブロック — else（§6.3）
elseBlock
    : CONDITION_MARKER SORE_IGAI COLON NEWLINE+
      stmtList
    ;

// 1行記法で許可されるアクション（§6.1）
singleAction
    : dialogue
    | funcCall
    | assignment
    | jump
    ;

// ============================================================
// 条件式（§4）
// ============================================================

condExpr
    : condOrExpr
    ;

condOrExpr
    : condAndExpr (MATAWA condAndExpr)*
    ;

condAndExpr
    : condPrimary (KATSU condPrimary)*
    ;

condPrimary
    : LPAREN condExpr RPAREN                // 括弧グループ（§4.7）
    | timeRange                             // 時間範囲（§4.6）
    | expr rangeOp                          // 範囲比較（§4.4）  例: 好感度 10~50
    | expr postfixCompOp                    // 後置比較（§4.3）  例: 好感度 50 以上
    | expr infixCompOp expr                 // 中置比較（§4.3）  例: 好感度 >= 50
    | expr EQ expr                          // 中置等価（§4.1）  例: 変数 == はい
    | expr NEQ expr                         // 中置不等価（§4.1）例: 変数 != はい
    | expr ASSIGN_EQ expr                   // 後置等価（§4.1）  例: 変数=はい（条件式内の=は等価比較）
    | expr                                  // 真偽値省略形（§4.2）例: 朝にユーザーと会話した
    ;

// 後置比較演算子（比較値を含む）: 好感度 50 以上 → expr=好感度, atom=50, IJOU
postfixCompOp
    : atom (MIMAN | IKA | IJOU | KOERU)
    ;

// 中置比較演算子
infixCompOp
    : LT | LTE | GT | GTE
    ;

// 範囲演算子（§4.4）: atom? ~ atom?
rangeOp
    : atom? TILDE atom?
    ;

// 時間範囲（§4.6）: TIME ~ TIME など
timeRange
    : TIME TILDE TIME   // 15:00~18:30
    | TILDE TIME        // ~8:00
    | TIME TILDE        // 18:30~
    ;

// ============================================================
// 算術式（§3.6）
// ============================================================

expr
    : unaryExpr ((PLUS | MINUS) unaryExpr)*
    ;

unaryExpr
    : (PLUS | MINUS)? mulExpr
    ;

mulExpr
    : atom ((STAR | SLASH | PERCENT) atom)*
    ;

// atom: 数値・変数・関数呼び出し・括弧など
// NUMBER IDENTIFIER* により「2月14日」のような数字始まり変数名を受理（§3.1実装注記参照）
atom
    : NUMBER IDENTIFIER*    // 数字始まりの名前（例: 2月14日に起動しなかった）または単純な数値
    | IDENTIFIER            // 通常の変数参照
    | HAI
    | IIE
    | stringLiteral
    | funcCall
    | LPAREN expr RPAREN
    ;

// ============================================================
// 関数呼び出し（§5）
// ============================================================

funcCall
    : FUNC_OPEN funcName funcArg* FUNC_CLOSE
    ;

funcName
    : IDENTIFIER
    ;

funcArg
    : IDENTIFIER ASSIGN_EQ funcArgValue    // 名前付き引数
    | funcArgValue                          // 位置引数
    ;

funcArgValue
    : NUMBER
    | HAI
    | IIE
    | stringLiteral
    | IDENTIFIER
    | LPAREN expr RPAREN    // 算術式（括弧必須）
    ;

// ============================================================
// セリフ（§6.4）
// ============================================================

dialogue
    : DIALOGUE_OPEN dialogueContent* DIALOGUE_CLOSE
    ;

// 文字列リテラル（代入右辺・関数引数など）
// dialogue と同じトークン列を持つが意味的に区別する
stringLiteral
    : DIALOGUE_OPEN dialogueContent* DIALOGUE_CLOSE
    ;

dialogueContent
    : DIALOGUE_TEXT
    | DIALOGUE_NEWLINE
    | DIALOGUE_ESCAPE_LANGLE    // ＜＜ → ＜
    | DIALOGUE_ESCAPE_RANGLE    // ＞＞ → ＞
    | DIALOGUE_ESCAPE_LBRACKET  // 「「 → 「
    | DIALOGUE_ESCAPE_RBRACKET  // 」」 → 」
    | funcCall                  // セリフ内関数呼び出し（§5.2）
    ;
