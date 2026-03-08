lexer grammar DaihonLexer;

// ============================================================
// DEFAULT mode
// ============================================================

// コメント（$$ または ＄＄ から行末まで）をスキップ
COMMENT
    : ('$$' | '\uFF04\uFF04') ~[\r\n]* -> skip
    ;

// 空白（全角・半角）をスキップ
WS
    : [ \t\u3000]+ -> skip
    ;

// 改行（文の区切り）
NEWLINE
    : '\r'? '\n'
    | '\r'
    ;

// ヘッダー行（### / ##）: HEADER モードへ遷移して名前を1トークンとして取得
// ### を ## より先にマッチさせる（最長一致規則で自動的に正しく動作）
SCENE_HEADER
    : ('###' | '\uFF03\uFF03\uFF03') -> pushMode(HEADER)
    ;

EVENT_HEADER
    : ('##' | '\uFF03\uFF03') -> pushMode(HEADER)
    ;

// 条件マーカー（※）と矢印（→）
CONDITION_MARKER : '\u203B' ;   // ※
ARROW            : '\u2192' ;   // →

// セリフ開始（「）→ DIALOGUE モードへ
DIALOGUE_OPEN : '\u300C' -> pushMode(DIALOGUE) ;   // 「

// 関数呼び出し開始（＜）→ FUNC モードへ
FUNC_OPEN : '\uFF1C' -> pushMode(FUNC) ;   // ＜

// ----------------------------------------------------------------
// 予約語（IDENTIFIER より先に定義して最長一致・先頭一致で確実に認識）
// ----------------------------------------------------------------
SCENE_OWARI   : '\u30B7\u30FC\u30F3\u304A\u308F\u308A' ;   // シーンおわり
OWARI         : '\u304A\u308F\u308A' ;                      // おわり
HAI           : '\u306F\u3044' ;                            // はい
IIE           : '\u3044\u3044\u3048' ;                      // いいえ
MIMAN         : '\u672A\u6E80' ;                            // 未満
IKA           : '\u4EE5\u4E0B' ;                            // 以下
IJOU          : '\u4EE5\u4E0A' ;                            // 以上
KOERU         : '\u8D85\u3048\u308B' ;                      // 超える
KATSU         : '\u304B\u3064' ;                            // かつ
MATAWA        : '\u307E\u305F\u306F' ;                      // または
ZENTEI_JOUKEN : '\u524D\u63D0\u6761\u4EF6' ;                // 前提条件
SHOKI_CHI     : '\u521D\u671F\u5024' ;                      // 初期値
AIZU          : '\u5408\u56F3' ;                            // 合図
JOUKEN        : '\u6761\u4EF6' ;                            // 条件
NARA          : '\u306A\u3089' ;                            // なら
ARUIHA        : '\u3042\u308B\u3044\u306F' ;                // あるいは
SORE_IGAI     : '\u305D\u308C\u4EE5\u5916' ;                // それ以外

// ----------------------------------------------------------------
// 演算子
// ----------------------------------------------------------------
EQ        : '==' ;
NEQ       : ('!' | '\uFF01') ('=' | '\uFF1D') ;   // != ！＝ ！= !＝
LTE       : '<=' ;
GTE       : '>=' ;
ASSIGN_EQ : ('=' | '\uFF1D') ;   // = ＝
LT        : '<' ;
GT        : '>' ;
PLUS      : '+' ;
MINUS     : '-' ;
STAR      : '*' ;
SLASH     : '/' ;
PERCENT   : '%' ;
TILDE     : ('~' | '\uFF5E') ;     // ~ ～
AT_SIGN   : ('\uFF20' | '@') ;     // ＠ @
COLON     : (':' | '\uFF1A') ;     // : ：
LPAREN    : ('(' | '\uFF08') ;     // ( （
RPAREN    : (')' | '\uFF09') ;     // ) ）
DOT       : '.' ;

// 時刻トークン（hh:mm 形式）— COLON より先に定義（最長一致で自動的に優先される）
TIME   : [0-9\uFF10-\uFF19]+ (':' | '\uFF1A') [0-9\uFF10-\uFF19]+ ;

// 数値リテラル（16進 / 8進 / 2進 / 10進・小数）
NUMBER
    : '0x' [0-9a-fA-F]+
    | '0o' [0-7]+
    | '0b' [01]+
    | [0-9\uFF10-\uFF19]+ (('.' | '\uFF0E') [0-9\uFF10-\uFF19]+)?
    ;

// 識別子（ひらがな・カタカナ・漢字・英字・数字・_ 先頭）
IDENTIFIER
    : (KANJI | KANA | ZEN_ALPHA | ASCII_ALPHA | ZEN_UNDER | ASCII_UNDER)
      (KANJI | KANA | ZEN_ALNUM | ASCII_ALNUM | ZEN_UNDER | ASCII_UNDER | JP_PUNCT)*
    ;

// ----------------------------------------------------------------
// フラグメント
// ----------------------------------------------------------------
fragment KANJI      : [\u4E00-\u9FFF\u3400-\u4DBF] ;      // CJK統合漢字・拡張A
fragment KANA       : [\u3040-\u309F\u30A0-\u30FF] ;      // ひらがな・カタカナ
fragment ZEN_ALPHA  : [\uFF21-\uFF3A\uFF41-\uFF5A] ;      // 全角英字 
fragment ZEN_ALNUM  : [\uFF10-\uFF19\uFF21-\uFF3A\uFF41-\uFF5A] ; // 全角英数字
fragment ASCII_ALPHA : [a-zA-Z] ;
fragment ASCII_ALNUM : [a-zA-Z0-9] ;
fragment ZEN_UNDER  : '\uFF3F' ;   // ＿
fragment ASCII_UNDER : '_' ;
fragment JP_PUNCT   : [\u2025\u2026] ; // ‥ … （日本語テキストで用いる省略記号）

// ============================================================
// HEADER mode（## / ### の直後：名前を1トークンで取得）
// ============================================================
mode HEADER;

HEADER_WS      : [ \t\u3000]+ -> skip ;
HEADER_NAME    : ~[\r\n]+ ;
HEADER_NEWLINE : ('\r'? '\n' | '\r') -> type(NEWLINE), popMode ;

// ============================================================
// DIALOGUE mode（「 〜 」 内）
// ============================================================
mode DIALOGUE;

// エスケープシーケンス（単体記号より先に定義して最長一致を確保）
DIALOGUE_ESCAPE_LANGLE   : '\uFF1C\uFF1C' ;   // ＜＜ → ＜
DIALOGUE_ESCAPE_RANGLE   : '\uFF1E\uFF1E' ;   // ＞＞ → ＞
DIALOGUE_ESCAPE_LBRACKET : '\u300C\u300C' ;   // 「「 → 「
DIALOGUE_ESCAPE_RBRACKET : '\u300D\u300D' ;   // 」」 → 」

// セリフ終了（」）
DIALOGUE_CLOSE     : '\u300D' -> popMode ;   // 」

// セリフ内の関数呼び出し開始（＜）→ FUNC モードへ（トークン型は FUNC_OPEN と同一）
DIALOGUE_FUNC_OPEN : '\uFF1C' -> type(FUNC_OPEN), pushMode(FUNC) ;   // ＜

// セリフ内の改行（保持）
DIALOGUE_NEWLINE   : '\r'? '\n' | '\r' ;

// セリフ本文（特殊文字を除くすべての文字）
// 除外: 」(U+300D) 「(U+300C) ＜(U+FF1C) ＞(U+FF1E) \r \n
DIALOGUE_TEXT      : ~[\u300D\u300C\uFF1C\uFF1E\r\n]+ ;

// ============================================================
// FUNC mode（＜ 〜 ＞ 内）
// ============================================================
mode FUNC;

// 空白スキップ
FUNC_WS : [ \t\u3000]+ -> skip ;

// 関数呼び出し終了（＞）
FUNC_CLOSE : '\uFF1E' -> popMode ;   // ＾

// 文字列リテラル（「〜」）→ FUNC_STR モードへ
FUNC_STR_OPEN : '\u300C' -> type(DIALOGUE_OPEN), pushMode(FUNC_STR) ;   // 「

// ネストされた関数呼び出し（＜）
FUNC_NESTED_OPEN : '\uFF1C' -> type(FUNC_OPEN), pushMode(FUNC) ;   // ＜

// 真偽値リテラル
FUNC_HAI : '\u306F\u3044'     -> type(HAI) ;   // はい
FUNC_IIE : '\u3044\u3044\u3048' -> type(IIE) ; // いいえ

// 演算子
FUNC_EQ        : '=='                                    -> type(EQ) ;
FUNC_NEQ       : ('!' | '\uFF01') ('=' | '\uFF1D')       -> type(NEQ) ;
FUNC_ASSIGN_EQ : ('=' | '\uFF1D')                        -> type(ASSIGN_EQ) ;
FUNC_LPAREN    : ('(' | '\uFF08')                        -> type(LPAREN) ;
FUNC_RPAREN    : (')' | '\uFF09')                        -> type(RPAREN) ;
FUNC_PLUS      : '+'  -> type(PLUS) ;
FUNC_MINUS     : '-'  -> type(MINUS) ;
FUNC_STAR      : '*'  -> type(STAR) ;
FUNC_SLASH     : '/'  -> type(SLASH) ;
FUNC_PERCENT   : '%'  -> type(PERCENT) ;

// 数値リテラル
FUNC_NUMBER
    : ( '0x' [0-9a-fA-F]+
      | '0o' [0-7]+
      | '0b' [01]+
      | [0-9\uFF10-\uFF19]+ (('.' | '\uFF0E') [0-9\uFF10-\uFF19]+)?
      ) -> type(NUMBER)
    ;

// 識別子（JP_PUNCT を含む — 省略記号 … を関数引数に使用できるようにする）
FUNC_IDENTIFIER
    : ( [\u4E00-\u9FFF\u3400-\u4DBF\u3040-\u309F\u30A0-\u30FF\uFF21-\uFF3A\uFF41-\uFF5A]
      | [a-zA-Z] | '\uFF3F' | '_'
      )
      ( [\u4E00-\u9FFF\u3400-\u4DBF\u3040-\u309F\u30A0-\u30FF\uFF10-\uFF19\uFF21-\uFF3A\uFF41-\uFF5A]
      | [a-zA-Z0-9] | '\uFF3F' | '_' | '\u2025' | '\u2026'
      )* -> type(IDENTIFIER)
    ;

// ============================================================
// FUNC_STR mode（関数引数内の文字列リテラル 「〜」）
// ============================================================
mode FUNC_STR;

FSTR_ESC_LBRACKET : '\u300C\u300C' -> type(DIALOGUE_ESCAPE_LBRACKET) ;  // 「「
FSTR_ESC_RBRACKET : '\u300D\u300D' -> type(DIALOGUE_ESCAPE_RBRACKET) ;  // 」」
FSTR_CLOSE        : '\u300D' -> type(DIALOGUE_CLOSE), popMode ;          // 」
FSTR_NEWLINE      : ('\r'? '\n' | '\r') -> type(DIALOGUE_NEWLINE) ;
FSTR_TEXT         : ~[\u300D\u300C\r\n]+ -> type(DIALOGUE_TEXT) ;
