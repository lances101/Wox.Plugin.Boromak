using System;
using System.Collections.Generic;
using System.Linq;

namespace Wox.Plugin.Boromak
{
    /// <summary>
    ///     Base class for commands
    ///     Handles queries and command execution by itself if not overriden
    ///     Designed to be used for complex trees of commands
    /// </summary>
    public abstract class CommandHandlerBase
    {
        protected string ForcedSubtitle;
        protected string ForcedTitle;
        //TODO: LOOK INTO Wox 1.3 parameter handling and change the command handling
        /// <summary>
        ///     Depth is used for getting command specific arguments from the query
        ///     <para />
        ///     Assuming that our hierarchy is the following: "Clock->Alarm->Set"
        ///     <para />
        ///     commandDepth for Set would be 3, which in the following query
        ///     <para />
        ///     "clock alarm set 15:00" corresponds to the 4th argument (15:00)
        /// </summary>
        protected int CommandDepth = -1;

        protected PluginInitContext Context;
        protected CommandHandlerBase ParentCommand;
        protected List<CommandHandlerBase> SubCommands = new List<CommandHandlerBase>();

        /// <summary>
        ///     CommandHandlerBase constructor.
        ///     Calculates the depth of this command upon creation.
        /// </summary>
        /// <param name="context">Wox plugin context</param>
        /// <param name="parent">parent of type CommandHandlerBase</param>
        protected CommandHandlerBase(PluginInitContext context, CommandHandlerBase parent = null)
        {
            Context = context;
            ParentCommand = parent;
            var temp = this;
           
            while (temp != null)
            {
                temp = temp.ParentCommand;
                CommandDepth++;
            }
        }

        public abstract string CommandAlias { get; }
        public abstract string CommandTitle { get; }
        public abstract string CommandDescription { get; }

        /// <summary>
        ///     Get an icon for this command
        ///     If this command does not have one, it will recursively search for it in its parents
        /// </summary>
        /// <returns>relative icon path</returns>
        public virtual string GetIconPath()
        {
            if (ParentCommand != null)
                return ParentCommand.GetIconPath();
            return Context.CurrentPluginMetadata.IcoPath;
        }

        /// <summary>
        ///     Executes the query for the current command
        ///     Override CommandQuery to change behavior
        /// </summary>
        /// <param name="query">query from parent command</param>
        /// <returns></returns>
        public List<Result> Query(Query query)
        {
            var results = new List<Result>();
            CommandQuery(query, ref results);
            ForcedTitle = "";
            ForcedSubtitle = "";
            return results;
        }

        /// <summary>
        ///     Executes before the actual query happens
        /// </summary>
        /// <param name="query"></param>
        protected virtual void PreQuery(Query query)
        {
        }

        /// <summary>
        ///     Executes after the query but before the results are returned.
        ///     If not overriden, resets forced titles.
        /// </summary>
        protected virtual void AfterQuery(Query query, ref List<Result> results)
        {
            ForcedTitle = "";
            ForcedSubtitle = "";
        }

        /// <summary>
        ///     If not overriden returns all subcommands of current command
        ///     and sets result action to call subcommand
        /// </summary>
        protected virtual List<Result> CommandQuery(Query query, ref List<Result> results)
        {
            var args = query.ActionParameters;

            if (args.Count - CommandDepth <= 0)
            {
                FillResultsWithSubcommands(args, results);
            }
            else
            {
                var specificHandler = SubCommands.FirstOrDefault(r => r.CommandAlias == args[CommandDepth].ToLower());
                if (specificHandler != null)
                {
                    results.AddRange(specificHandler.Query(query));
                }
                else
                {
                    FillResultsWithSubcommands(args, results, args[CommandDepth].ToLower());
                }
            }
            return results;
        }

        /// <summary>
        ///     Fills results with subcommands from parent
        /// </summary>
        /// <param name="args">arguments from query</param>
        /// <param name="results">list of results to fill</param>
        /// <param name="filterAlias">string to filter commands by name</param>
        private void FillResultsWithSubcommands(List<string> args, List<Result> results, string filterAlias = "")
        {
            foreach (var subcommand in SubCommands)
            {
                if (filterAlias != "" && !subcommand.CommandAlias.Contains(filterAlias)) continue;

                results.Add(new Result
                {
                    Title = subcommand.CommandTitle,
                    SubTitle = subcommand.CommandDescription,
                    IcoPath = subcommand.GetIconPath(),
                    Action = e => subcommand.Execute(args)
                });
            }
        }

        /// <summary>
        ///     Does a check so that the current command actually has a parameter for execution
        ///     and then executes the CommandExecution function in a try/catch
        ///     If the command threw an argument exception displays a message through
        ///     _forcedTitle and _forcedSubtitle
        /// </summary>
        /// <param name="args">list of arguments</param>
        /// <returns></returns>
        public bool Execute(List<string> args)
        {
            var shouldHide = false;
            ForcedTitle = "";
            ForcedSubtitle = "";
            if (args.Count > CommandDepth)
            {
                try
                {
                    shouldHide = CommandExecution(args);
                }
                catch (ArgumentException e)
                {
                    ForcedTitle = "An error has occured";
                    ForcedSubtitle = e.Message;
                    RequeryPlugin(args);
                    return false;
                }
            }
            RequeryCurrentCommand();

            return shouldHide;
        }

        /// <summary>
        ///     If not overriden will requery with the current command without arguments
        ///     Any parameter checks go here. If an argument was invalid you must
        ///     throw a ArgumentException with the error message
        /// </summary>
        /// <param name="args">query parameters</param>
        /// <returns type="boolean">should Wox hide after execution </returns>
        protected virtual bool CommandExecution(List<string> args)
        {
            RequeryCurrentCommand();
            return false;
        }

        /// <summary>
        ///     Changes query using the provided argument strings
        /// </summary>
        protected void RequeryPlugin(List<string> args, bool submit = false )
        {
            Context.API.ChangeQuery(
                String.Format("{0} {1}", 
                    Context.CurrentPluginMetadata.ActionKeyword,
                    String.Join(" ", args.ToArray())
                ), 
                submit);
        }

        /// <summary>
        ///     Changes the query to the current command
        /// </summary>
        /// <param name="args">arguments for the command. should not include the command name</param>
        /// <param name="submit">should we submit after changing the query</param>
        protected void RequeryCurrentCommand(List<string> args = null, bool submit = false)
        {
            Context.API.ChangeQuery(
                String.Format("{0} {1}", 
                    GetCommandPath(),
                    args == null? "" : String.Join(" ", args.ToArray())
                ), 
                submit);
        }

        /// <summary>
        ///     Calculates the path to the current command
        /// </summary>
        /// <returns>path to this command without action keyword</returns>
        protected string GetCommandPath()
        {
            var path = String.Empty;
            var temp = this;
            while (temp != null)
            {
                if (temp.CommandDepth < 1) break;
                if (!String.IsNullOrEmpty(temp.CommandAlias))
                    path = path.Insert(0, temp.CommandAlias + " ");
                temp = temp.ParentCommand;
            }
            path = path.Insert(0, Context.CurrentPluginMetadata.ActionKeyword + " ");
            path = path.TrimEnd();
            return path;
        }
    }
}