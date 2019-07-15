using System;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Collections.Generic;


public class Tokenizer : IConsumer<Token>
{
    protected Token current;

    public Token Current {
        get => current;
    }

    private Queue<Token> reconsumeQueue;

    protected int position;

    public int Position {
        get => position;
    }

    protected StringConsumer consumer;

    public Tokenizer(StringConsumer consumer)
    {
        this.consumer = consumer;

        reconsumeQueue = new Queue<Token>();

        current = new Token('\0', TokenKind.delim, consumer.Position);
    }

    public Tokenizer(System.IO.FileInfo fileInfo) : this(new StringConsumer(fileInfo))
    { }

    public Tokenizer(IEnumerable<char> collection) : this(new StringConsumer(collection))
    { }

    public Tokenizer(IEnumerable<string> collection) : this(new StringConsumer(collection))
    { }

    public void Reconsume() => reconsumeQueue.Enqueue(current);

    public Token Peek() => Peek(1)[0];

    public List<Token> Peek(int n=1) {
        // create a new (dee-copy of) tokenizer from this one
        var tokenizer = new Tokenizer(new StringConsumer(this.consumer));

        // the output list
        var output = new List<Token>();

        // consume `n` tokens and add them to the output
        for (int i = 0; i < n; i++)
        {
            output.Add(tokenizer.Consume());
        }

        return output;
    }

    public Token Consume(out bool success) {
        var token = Consume();

        success = token != TokenKind.EOF;

        return token;
    }

    public Token Consume() {

        // If we are instructed to reconsume the last token, then return the last token consumed
        if (reconsumeQueue.Count != 0) {
            return reconsumeQueue.Dequeue();
        }

        // Updates the position
        position++;

        // Consume a character from the LinesConsumer object
        var currChar = consumer.Consume();

        // While there is whitespace, consume it
        while (Char.IsWhiteSpace(currChar)) {
            currChar = consumer.Consume();
        }

        // if you want to preserve whitespace, you could do an if before the while loop and then return a whitespace token
        // although, you'll also need to modify the parser (in particular Parser.ConsumeValue or Parser.ToPostFixNotation)
        // because it might not correctly detect function calls and a lot of other thing

        // If the character is U+0003 END OF TRANSMISSION, it means there is nothing left to consume. Return an EOF token
        if (currChar == '\u0003' || currChar == '\0') return new Token(currChar, TokenKind.EOF, consumer.Position);

        // If the chacracter is a digit
        if (Char.IsDigit(currChar))
        {
            // Reconsume the current character
            consumer.Reconsume();

            // Consume a number token
            current = ConsumeNumberToken();

            // And return it
            return current;
        }

        // If the character is '+', '-', or '.', followed by a digit
        if (currChar == '.' && Char.IsDigit(consumer.Peek()))
        {
            // Reconsume the current character
            consumer.Reconsume();

            // Consume a number token
            current = ConsumeNumberToken();

            return current;
        }

        // If the character is a single or double quote
        if (currChar == '\'' || currChar == '"') {
            // Consume a string token and return it
            current = ConsumeStringToken(currChar);

            return current;
        }

        // If the character is a letter or a low line
        if (Char.IsLetter(currChar) || currChar == '_') {

            // Reconsume the current character
            consumer.Reconsume();

            // Consume an identifier token and return it
            current = ConsumeIdentToken();

            return current;
        }

        // If the character is '+' or '-'
        if (currChar == '+' || currChar == '-') {

            // if the next character is the same as now ('+' and '+' for example), then it is either an increment or a decrement ("++" and "--")
            if (consumer.Peek() == currChar) {

                // return a new operator token with precedence 7
                current = new OperatorToken(currChar +""+ consumer.Consume(), Precedence.Unary, false, consumer.Position);

                return current;
            }

            // return a new operator token with precedence 2
            current = new OperatorToken(currChar, Precedence.Addition, true, consumer.Position);

            return current;
        }

        // If the character is '*' or '/'
        if (currChar == '*' || currChar == '/') {

            // return a new operator token with precedence 3
            current = new OperatorToken(currChar, Precedence.Division, true, consumer.Position);

            return current;
        }

        // if the character is '^'
        if (currChar == '^') {

            // return a new operator token with precedence 4
            current = new OperatorToken(currChar, Precedence.Power, false, consumer.Position);

            return current;
        }

        // if the character is '!'
        if (currChar == '!') {

            if (consumer.Peek() == '=') {
                current = new OperatorToken(currChar +""+ consumer.Consume(), Precedence.NotEqual, false, consumer.Position);

                return current;
            }

            // return a new operator token with precedence 5
            current = new OperatorToken(currChar, Precedence.Unary, false, consumer.Position);

            return current;
        }

        // if the current and next character form "&&"
        if (currChar == '&' && consumer.Peek() == '&') {

            // return a new operator token with precedence 5
            current = new OperatorToken(currChar +""+ consumer.Consume(), Precedence.LogicalAND, true, consumer.Position);

            return current;
        }

        // if the current and next character form "||"
        if (currChar == '|' && consumer.Peek() == '|') {

            // return a new operator token with precedence 5
            current = new OperatorToken(currChar +""+ consumer.Consume(), Precedence.LogicalOR, true, consumer.Position);

            return current;
        }

        // if the current and next character for "=="
        if (currChar == '=') {

            if (consumer.Peek() == '=') {
                current = new OperatorToken(currChar +""+ consumer.Consume(), Precedence.Equal, true, consumer.Position);

                return current;
            }

            current = new OperatorToken(currChar, Precedence.Assignement, false, consumer.Position);

            return current;
        }

        if (currChar == '>') {

            if (consumer.Peek() == '=') {
                current = new OperatorToken(currChar +""+ consumer.Consume(), Precedence.GreaterThanOrEqual, true, consumer.Position);

                return current;
            }

            current = new OperatorToken(currChar, Precedence.GreaterThan, true, consumer.Position);

            return current;
        }

        if (currChar == '<') {

            if (consumer.Peek() == '=') {
                current = new OperatorToken(currChar +""+ consumer.Consume(), Precedence.LessThanOrEqual, true, consumer.Position);

                return current;
            }

            current = new OperatorToken(currChar, Precedence.LessThan, true, consumer.Position);

            return current;
        }

        // return a new delim token
        current = new Token(currChar, TokenKind.delim, consumer.Position);

        return current;
    }

    protected ComplexToken ConsumeIdentToken() {

        // consume a character
        var currChar = consumer.Consume();

        // the output token
        var output = new ComplexToken("", TokenKind.ident, consumer.Position);

        // if the character is not a letter or a low line
        if (!(Char.IsLetter(currChar) || currChar == '_')) {
            throw new Exception("An identifier cannot start with the character " + currChar);
        }

        // while the current character is a letter, a digit, or a low line
        while (Char.IsLetterOrDigit(currChar) || currChar == '_') {

            // add it to the value of output
            output.Add(currChar);

            // consume a character
            currChar = consumer.Consume();
        }

        // reconsume the last token (which is not a letter, a digit, or a low line,
        // since our while loop has exited) to make sure it is processed by the tokenizer
        consumer.Reconsume();

        // return the output token
        return output;
    }

    protected ComplexToken ConsumeStringToken(char endingDelimiter) {

        // consume a character
        var currChar = consumer.Consume();

        // the output token
        var output = new ComplexToken("", TokenKind.@string, consumer.Position);

        // while the current character is not the ending delimiter
        while (currChar != endingDelimiter) {

            // add it to the value of output
            output.Add(currChar);

            // consume a character
            currChar = consumer.Consume();
        }

        // return the output token
        return output;
    }

    protected NumberToken ConsumeNumberToken() {

        // consume a character
        var currChar = consumer.Consume();

        // the output token
        var output = new NumberToken("", consumer.Position);

        // while the current character is a digit
        while (Char.IsDigit(currChar)) {

            // add it to the value of output
            output.Add(currChar);

            // consume a character
            currChar = consumer.Consume();
        }

        // if the character is '.'
        if (currChar == '.')
        {
            // add it to the value of output
            output.Add(currChar);

            // consume a character
            currChar = consumer.Consume();
        }

        // while the current character is a digit
        while (Char.IsDigit(currChar)) {

            // add it to the value of output
            output.Add(currChar);

            // consume a character
            currChar = consumer.Consume();
        }

        // if the character is an 'e' or an 'E'
        if (currChar == 'e' || currChar == 'E') {

            // add the e/E to the output
            output.Add(currChar);

            // consume a character
            currChar = consumer.Consume();


            // if the character is a '+' or a '-'
            if (currChar == '+' || currChar == '-') {

                // add it to the value of output
                output.Add(currChar);

                // consume a character
                currChar = consumer.Consume();
            }

            // while the current character is a digit
            while (Char.IsDigit(currChar)) {

                // add it to the value of output
                output.Add(currChar);

                // consume a character
                currChar = consumer.Consume();
            }
        }

        // if the character is '.'
        if (currChar == '.') {
            throw new Exception("Unexpected decimal separator at position " + consumer.Position);
        }

        // if the character is 'e' or 'E'
        if (currChar == 'e' || currChar == 'E') {
            throw new Exception("Unexpected power of ten separator at position " + consumer.Position);
        }

        consumer.Reconsume();

        return output;
    }
}