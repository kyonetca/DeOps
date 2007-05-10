/********************************************************************************

	De-Ops: Decentralized Operations
	Copyright (C) 2006 John Marshall Group, Inc.

	By contributing code you grant John Marshall Group an unlimited, non-exclusive
	license to your contribution.

	For support, questions, commercial use, etc...
	E-Mail: swabby@c0re.net

********************************************************************************/

using System;
using System.Drawing;
using System.Collections;
using System.ComponentModel;
using System.Text;
using System.Windows.Forms;

using DeOps.Implementation;

namespace DeOps.Interface.Tools
{
	 
	/// <summary>
	/// Summary description for ConsoleForm.
	/// </summary>
	internal class ConsoleForm : System.Windows.Forms.Form
	{
        OpCore Core;

		private System.Windows.Forms.RichTextBox RichTextBoxConsole;
		private System.Windows.Forms.TextBox TextBoxCommand;
		/// <summary>
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.Container components = null;

		internal delegate void UpdateConsoleHandler(string message);
		internal UpdateConsoleHandler UpdateConsole;

		bool ShowPackets;


		internal ConsoleForm(OpCore core)
		{			
			//
			// Required for Windows Form Designer support
			//
			InitializeComponent();

			Core = core;

			UpdateConsole = new UpdateConsoleHandler(AsyncUpdateConsole);

			Text = "Console (" + Core.User.Settings.ScreenName + ")";

			RefreshLog();
		}

		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		protected override void Dispose( bool disposing )
		{
			if( disposing )
			{
				if(components != null)
				{
					components.Dispose();
				}
			}
			base.Dispose( disposing );
		}

		#region Windows Form Designer generated code
		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			System.Resources.ResourceManager resources = new System.Resources.ResourceManager(typeof(ConsoleForm));
			this.RichTextBoxConsole = new System.Windows.Forms.RichTextBox();
			this.TextBoxCommand = new System.Windows.Forms.TextBox();
			this.SuspendLayout();
			// 
			// RichTextBoxConsole
			// 
			this.RichTextBoxConsole.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
				| System.Windows.Forms.AnchorStyles.Left) 
				| System.Windows.Forms.AnchorStyles.Right)));
			this.RichTextBoxConsole.BackColor = System.Drawing.Color.Black;
			this.RichTextBoxConsole.BorderStyle = System.Windows.Forms.BorderStyle.None;
			this.RichTextBoxConsole.Font = new System.Drawing.Font("Lucida Console", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((System.Byte)(0)));
			this.RichTextBoxConsole.ForeColor = System.Drawing.Color.White;
			this.RichTextBoxConsole.Location = new System.Drawing.Point(8, 8);
			this.RichTextBoxConsole.Name = "RichTextBoxConsole";
			this.RichTextBoxConsole.ReadOnly = true;
			this.RichTextBoxConsole.Size = new System.Drawing.Size(448, 264);
			this.RichTextBoxConsole.TabIndex = 1;
			this.RichTextBoxConsole.Text = "";
			// 
			// TextBoxCommand
			// 
			this.TextBoxCommand.AcceptsReturn = true;
			this.TextBoxCommand.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
				| System.Windows.Forms.AnchorStyles.Right)));
			this.TextBoxCommand.BackColor = System.Drawing.Color.Black;
			this.TextBoxCommand.BorderStyle = System.Windows.Forms.BorderStyle.None;
			this.TextBoxCommand.Font = new System.Drawing.Font("Lucida Console", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((System.Byte)(0)));
			this.TextBoxCommand.ForeColor = System.Drawing.Color.White;
			this.TextBoxCommand.Location = new System.Drawing.Point(8, 280);
			this.TextBoxCommand.Multiline = true;
			this.TextBoxCommand.Name = "TextBoxCommand";
			this.TextBoxCommand.Size = new System.Drawing.Size(440, 20);
			this.TextBoxCommand.TabIndex = 0;
			this.TextBoxCommand.Text = "";
			this.TextBoxCommand.KeyDown += new System.Windows.Forms.KeyEventHandler(this.TextBoxCommand_KeyDown);
			// 
			// ConsoleForm
			// 
			this.AutoScaleBaseSize = new System.Drawing.Size(5, 13);
			this.BackColor = System.Drawing.Color.Black;
			this.ClientSize = new System.Drawing.Size(456, 294);
			this.Controls.Add(this.TextBoxCommand);
			this.Controls.Add(this.RichTextBoxConsole);
			this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
			this.Name = "ConsoleForm";
			this.Text = "Console";
			this.Closing += new System.ComponentModel.CancelEventHandler(this.ConsoleForm_Closing);
			this.ResumeLayout(false);

		}
		#endregion

		private void TextBoxCommand_KeyDown(object sender, System.Windows.Forms.KeyEventArgs e)
		{
			if(e.KeyCode == Keys.Return)
			{
				TextBoxCommand.Text = TextBoxCommand.Text.Replace("\r\n", "");
				
				if(TextBoxCommand.Text.ToLower() == "showpackets")
				{
					ShowPackets = !ShowPackets;
					RefreshLog();
				}
				else
					Core.ConsoleCommand(TextBoxCommand.Text);
				
				TextBoxCommand.SelectionStart = 0;
				TextBoxCommand.Text = "";
				TextBoxCommand.Focus();
			}
		}

		internal void RefreshLog()
		{
			RichTextBoxConsole.Clear();

			//lock(Login.ConsoleText.SyncRoot)
				foreach(string  message in (Queue) Core.ConsoleText)
					//if( !entry.Packet || (entry.Packet && ShowPackets))
						RichTextBoxConsole.AppendText(message + "\n");
		}

		internal void AsyncUpdateConsole(string message)
		{
			//if( !entry.Packet || (entry.Packet && ShowPackets))
			//{
				RichTextBoxConsole.AppendText( message + "\n");

				if( TextBoxCommand.Focused )
				{
					RichTextBoxConsole.Focus();
					RichTextBoxConsole.SelectionStart = RichTextBoxConsole.Text.Length;
					RichTextBoxConsole.ScrollToCaret();

					TextBoxCommand.Focus();
				}
			//}
		}

		private void ConsoleForm_Closing(object sender, System.ComponentModel.CancelEventArgs e)
		{
			Core.GuiConsole = null;
		}

	}
}
