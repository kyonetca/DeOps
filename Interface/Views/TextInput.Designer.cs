namespace RiseOp.Interface
{
    partial class TextInput
    {
        /// <summary> 
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary> 
        /// Required method for Designer support - do not modify 
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(TextInput));
            this.FontToolStrip = new System.Windows.Forms.ToolStrip();
            this.toolStripSeparator1 = new System.Windows.Forms.ToolStripSeparator();
            this.BoldButton = new System.Windows.Forms.ToolStripButton();
            this.ItalicsButton = new System.Windows.Forms.ToolStripButton();
            this.UnderlineButton = new System.Windows.Forms.ToolStripButton();
            this.toolStripSeparator2 = new System.Windows.Forms.ToolStripSeparator();
            this.RichTextButton = new System.Windows.Forms.ToolStripButton();
            this.ColorsButton = new System.Windows.Forms.ToolStripButton();
            this.InputBox = new System.Windows.Forms.RichTextBox();
            this.toolStripDropDownButton1 = new System.Windows.Forms.ToolStripDropDownButton();
            this.plainTextToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.richTextToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.SendFileButton = new System.Windows.Forms.ToolStripButton();
            this.BlockButton = new System.Windows.Forms.ToolStripButton();
            this.FontToolStrip.SuspendLayout();
            this.SuspendLayout();
            // 
            // FontToolStrip
            // 
            this.FontToolStrip.GripStyle = System.Windows.Forms.ToolStripGripStyle.Hidden;
            this.FontToolStrip.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripDropDownButton1,
            this.toolStripSeparator1,
            this.BoldButton,
            this.ItalicsButton,
            this.UnderlineButton,
            this.RichTextButton,
            this.ColorsButton,
            this.toolStripSeparator2,
            this.SendFileButton,
            this.BlockButton});
            this.FontToolStrip.Location = new System.Drawing.Point(0, 0);
            this.FontToolStrip.Name = "FontToolStrip";
            this.FontToolStrip.Size = new System.Drawing.Size(299, 25);
            this.FontToolStrip.TabIndex = 0;
            this.FontToolStrip.Text = "toolStrip1";
            // 
            // toolStripSeparator1
            // 
            this.toolStripSeparator1.Name = "toolStripSeparator1";
            this.toolStripSeparator1.Size = new System.Drawing.Size(6, 25);
            // 
            // BoldButton
            // 
            this.BoldButton.CheckOnClick = true;
            this.BoldButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.BoldButton.Image = ((System.Drawing.Image)(resources.GetObject("BoldButton.Image")));
            this.BoldButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.BoldButton.Name = "BoldButton";
            this.BoldButton.Size = new System.Drawing.Size(23, 22);
            this.BoldButton.Text = "Bold";
            this.BoldButton.Click += new System.EventHandler(this.BoldButton_Click);
            // 
            // ItalicsButton
            // 
            this.ItalicsButton.CheckOnClick = true;
            this.ItalicsButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.ItalicsButton.Image = ((System.Drawing.Image)(resources.GetObject("ItalicsButton.Image")));
            this.ItalicsButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.ItalicsButton.Name = "ItalicsButton";
            this.ItalicsButton.Size = new System.Drawing.Size(23, 22);
            this.ItalicsButton.Text = "Italics";
            this.ItalicsButton.Click += new System.EventHandler(this.ItalicsButton_Click);
            // 
            // UnderlineButton
            // 
            this.UnderlineButton.CheckOnClick = true;
            this.UnderlineButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.UnderlineButton.Image = ((System.Drawing.Image)(resources.GetObject("UnderlineButton.Image")));
            this.UnderlineButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.UnderlineButton.Name = "UnderlineButton";
            this.UnderlineButton.Size = new System.Drawing.Size(23, 22);
            this.UnderlineButton.Text = "Underline";
            this.UnderlineButton.Click += new System.EventHandler(this.UnderlineButton_Click);
            // 
            // toolStripSeparator2
            // 
            this.toolStripSeparator2.Name = "toolStripSeparator2";
            this.toolStripSeparator2.Size = new System.Drawing.Size(6, 25);
            // 
            // RichTextButton
            // 
            this.RichTextButton.CheckOnClick = true;
            this.RichTextButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.RichTextButton.Image = ((System.Drawing.Image)(resources.GetObject("RichTextButton.Image")));
            this.RichTextButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.RichTextButton.Name = "RichTextButton";
            this.RichTextButton.Size = new System.Drawing.Size(23, 22);
            this.RichTextButton.Text = "Enable Rich Text";
            this.RichTextButton.CheckedChanged += new System.EventHandler(this.RichTextButton_CheckedChanged);
            this.RichTextButton.Click += new System.EventHandler(this.RichTextButton_Click);
            // 
            // ColorsButton
            // 
            this.ColorsButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.ColorsButton.Image = ((System.Drawing.Image)(resources.GetObject("ColorsButton.Image")));
            this.ColorsButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.ColorsButton.Name = "ColorsButton";
            this.ColorsButton.Size = new System.Drawing.Size(23, 22);
            this.ColorsButton.Text = "Colors";
            this.ColorsButton.Click += new System.EventHandler(this.ColorsButton_Click);
            // 
            // InputBox
            // 
            this.InputBox.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.InputBox.Dock = System.Windows.Forms.DockStyle.Fill;
            this.InputBox.Font = new System.Drawing.Font("Tahoma", 9.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.InputBox.Location = new System.Drawing.Point(0, 25);
            this.InputBox.Name = "InputBox";
            this.InputBox.Size = new System.Drawing.Size(299, 39);
            this.InputBox.TabIndex = 1;
            this.InputBox.Text = "";
            this.InputBox.KeyDown += new System.Windows.Forms.KeyEventHandler(this.InputBox_KeyDown);
            this.InputBox.SelectionChanged += new System.EventHandler(this.InputBox_SelectionChanged);
            this.InputBox.LinkClicked += new System.Windows.Forms.LinkClickedEventHandler(this.InputBox_LinkClicked);
            // 
            // toolStripDropDownButton1
            // 
            this.toolStripDropDownButton1.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.toolStripDropDownButton1.DropDownItems.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.plainTextToolStripMenuItem,
            this.richTextToolStripMenuItem});
            this.toolStripDropDownButton1.Image = ((System.Drawing.Image)(resources.GetObject("toolStripDropDownButton1.Image")));
            this.toolStripDropDownButton1.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.toolStripDropDownButton1.Name = "toolStripDropDownButton1";
            this.toolStripDropDownButton1.Size = new System.Drawing.Size(29, 22);
            this.toolStripDropDownButton1.Text = "toolStripDropDownButton1";
            // 
            // plainTextToolStripMenuItem
            // 
            this.plainTextToolStripMenuItem.Image = ((System.Drawing.Image)(resources.GetObject("plainTextToolStripMenuItem.Image")));
            this.plainTextToolStripMenuItem.Name = "plainTextToolStripMenuItem";
            this.plainTextToolStripMenuItem.Size = new System.Drawing.Size(152, 22);
            this.plainTextToolStripMenuItem.Text = "Plain Text";
            // 
            // richTextToolStripMenuItem
            // 
            this.richTextToolStripMenuItem.Image = ((System.Drawing.Image)(resources.GetObject("richTextToolStripMenuItem.Image")));
            this.richTextToolStripMenuItem.Name = "richTextToolStripMenuItem";
            this.richTextToolStripMenuItem.Size = new System.Drawing.Size(152, 22);
            this.richTextToolStripMenuItem.Text = "Rich Text";
            // 
            // SendFileButton
            // 
            this.SendFileButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.SendFileButton.Image = ((System.Drawing.Image)(resources.GetObject("SendFileButton.Image")));
            this.SendFileButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.SendFileButton.Name = "SendFileButton";
            this.SendFileButton.Size = new System.Drawing.Size(23, 22);
            this.SendFileButton.Text = "Send File";
            this.SendFileButton.Click += new System.EventHandler(this.SendFileButton_Click);
            // 
            // BlockButton
            // 
            this.BlockButton.Alignment = System.Windows.Forms.ToolStripItemAlignment.Right;
            this.BlockButton.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Image;
            this.BlockButton.Image = ((System.Drawing.Image)(resources.GetObject("BlockButton.Image")));
            this.BlockButton.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.BlockButton.Name = "BlockButton";
            this.BlockButton.Size = new System.Drawing.Size(23, 22);
            this.BlockButton.Text = "Block User";
            this.BlockButton.Click += new System.EventHandler(this.BlockButton_Click);
            // 
            // TextInput
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.InputBox);
            this.Controls.Add(this.FontToolStrip);
            this.Name = "TextInput";
            this.Size = new System.Drawing.Size(299, 64);
            this.FontToolStrip.ResumeLayout(false);
            this.FontToolStrip.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ToolStrip FontToolStrip;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator1;
        private System.Windows.Forms.ToolStripButton BoldButton;
        private System.Windows.Forms.ToolStripButton ItalicsButton;
        private System.Windows.Forms.ToolStripButton UnderlineButton;
        private System.Windows.Forms.ToolStripSeparator toolStripSeparator2;
        private System.Windows.Forms.ToolStripButton ColorsButton;
        internal System.Windows.Forms.RichTextBox InputBox;
        private System.Windows.Forms.ToolStripButton RichTextButton;
        private System.Windows.Forms.ToolStripDropDownButton toolStripDropDownButton1;
        private System.Windows.Forms.ToolStripMenuItem plainTextToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem richTextToolStripMenuItem;
        private System.Windows.Forms.ToolStripButton SendFileButton;
        private System.Windows.Forms.ToolStripButton BlockButton;
    }
}
